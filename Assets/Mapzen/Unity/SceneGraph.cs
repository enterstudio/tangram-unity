﻿using System;
using System.Linq;
using Mapzen;
using UnityEngine;
using System.Collections.Generic;

namespace Mapzen.Unity
{
    public class SceneGraph
    {
        // Merged mesh data for each game object group
        private Dictionary<GameObject, MeshData> gameObjectMeshData;
        // The root game object
        private GameObject mapRegion;
        // Group options for grouping
        private SceneGroupType groupOptions;
        // The leaf group of the hierarchy
        private SceneGroupType leafGroup;
        // The game object options (physics, static, ...)
        private GameObjectOptions gameObjectOptions;
        // The list of feature mesh generated by the tile task
        private List<FeatureMesh> features;

        public SceneGraph(GameObject mapRegion, SceneGroupType groupOptions, GameObjectOptions gameObjectOptions, List<FeatureMesh> features)
        {
            this.gameObjectMeshData = new Dictionary<GameObject, MeshData>();
            this.mapRegion = mapRegion;
            this.groupOptions = groupOptions;
            this.gameObjectOptions = gameObjectOptions;
            this.features = features;
            this.leafGroup = groupOptions.GetLeaf();
        }

        private GameObject AddGameObjectGroup(SceneGroupType groupType, GameObject parentGameObject, FeatureMesh featureMesh)
        {
            GameObject gameObject = null;

            string name = featureMesh.GetName(groupType);

            // No name for this group in the feature, use the parent name
            if (name.Length == 0)
            {
                name = parentGameObject.name;
            }

            Transform transform = parentGameObject.transform.Find(name);

            if (transform == null)
            {
                // No children for this name, create a new game object
                gameObject = new GameObject(name);
                gameObject.transform.parent = parentGameObject.transform;
            }
            else
            {
                // Reuse the game object found the the group name in the hierarchy
                gameObject = transform.gameObject;
            }

            return gameObject;
        }

        private void MergeMeshData(GameObject gameObject, FeatureMesh featureMesh)
        {
            // Merge the mesh data from the feature for the game object
            if (gameObjectMeshData.ContainsKey(gameObject))
            {
                gameObjectMeshData[gameObject].Merge(featureMesh.Mesh);
            }
            else
            {
                MeshData data = new MeshData();
                data.Merge(featureMesh.Mesh);
                gameObjectMeshData.Add(gameObject, data);
            }
        }

        public void Generate()
        {
            // 1. Generate the game object hierarchy in the scene graph
            if (groupOptions == SceneGroupType.Nothing)
            {
                // Merge every game object created to the 'root' element being the map region
                foreach (var featureMesh in features)
                {
                    MergeMeshData(mapRegion, featureMesh);
                }
            }
            else
            {
                GameObject currentGroup;

                // Generate all game object with the appropriate hiarchy
                foreach (var featureMesh in features)
                {
                    currentGroup = mapRegion;

                    foreach (SceneGroupType group in Enum.GetValues(typeof(SceneGroupType)))
                    {
                        // Exclude 'nothing' and 'everything' group
                        if (group == SceneGroupType.Nothing || group == SceneGroupType.Everything)
                        {
                            continue;
                        }

                        if (groupOptions.Includes(group))
                        {
                            // Use currentGroup as the parentGroup for the current generation
                            var parentGroup = currentGroup;
                            var newGroup = AddGameObjectGroup(group, parentGroup, featureMesh);

                            // Top down of the hierarchy, merge the game objects
                            if (group == leafGroup)
                            {
                                MergeMeshData(newGroup, featureMesh);
                            }

                            currentGroup = newGroup;
                        }
                    }
                }
            }

            // 2. Initialize game objects and associate their components (physics, rendering)
            foreach (var pair in gameObjectMeshData)
            {
                var meshData = pair.Value;
                var root = pair.Key;

                // Create one game object per mesh object 'bucket', each bucket is ensured to
                // have less that 65535 vertices (valid under Unity mesh max vertex count).
                for (int i = 0; i < meshData.Meshes.Count; ++i)
                {
                    var meshBucket = meshData.Meshes[i];
                    GameObject gameObject;

                    if (meshData.Meshes.Count > 1)
                    {
                        gameObject = new GameObject(root.name + "_Part" + i);
                        gameObject.transform.parent = root.transform;
                    }
                    else
                    {
                        gameObject = root.gameObject;
                    }

                    gameObject.isStatic = gameObjectOptions.IsStatic;

                    var mesh = new Mesh();

                    mesh.SetVertices(meshBucket.Vertices);
                    mesh.SetUVs(0, meshBucket.UVs);
                    mesh.subMeshCount = meshBucket.Submeshes.Count;
                    for (int s = 0; s < meshBucket.Submeshes.Count; s++)
                    {
                        mesh.SetTriangles(meshBucket.Submeshes[s].Indices, s);
                    }
                    mesh.RecalculateNormals();

                    // Associate the mesh filter and mesh renderer components with this game object
                    var materials = meshBucket.Submeshes.Select(s => s.Material).ToArray();
                    var meshFilterComponent = gameObject.AddComponent<MeshFilter>();
                    var meshRendererComponent = gameObject.AddComponent<MeshRenderer>();
                    meshRendererComponent.materials = materials;
                    meshFilterComponent.mesh = mesh;

                    if (gameObjectOptions.GeneratePhysicMeshCollider)
                    {
                        var meshColliderComponent = gameObject.AddComponent<MeshCollider>();
                        meshColliderComponent.material = gameObjectOptions.PhysicMaterial;
                        meshColliderComponent.sharedMesh = mesh;
                    }
                }
            }
        }
    }
}
