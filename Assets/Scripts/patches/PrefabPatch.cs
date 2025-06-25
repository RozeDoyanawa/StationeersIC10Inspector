using System;
using System.Collections.ObjectModel;
using Assets.Scripts.Objects;
using Assets.Scripts.UI;
using HarmonyLib;
using StationeersMods.Interface;
using UnityEngine;

namespace ridorana.IC10Inspector.patches
{
    [HarmonyPatch]
    public class PrefabPatch
    {
        public static ReadOnlyCollection<GameObject> prefabs { get; set; }
        [HarmonyPatch(typeof(Prefab), "LoadAll")]
        public static void Prefix()
        {
            try
            {
                Debug.Log("Prefab Patch started");
                foreach (var gameObject in prefabs)
                {
                    Thing thing = gameObject.GetComponent<Thing>();
                    if (thing != null)
                    {
                        if (thing is IPatchable patchable) {
                            patchable.PatchOnLoad();
                        }
                        Blueprintify(thing);
                        Debug.Log(gameObject.name + " added to WorldManager");
                        WorldManager.Instance.SourcePrefabs.Add(thing);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.Log(ex.Message);
                Debug.LogException(ex);
            }
        }
        
        private static GameObject blueprintContainer;
        
        private static void Blueprintify(Thing thing)
        {
            if (thing.Blueprint != null)
            {
                // don't overwrite existing blueprints, but generate the wireframe for our own
                GenerateWireframe(thing.Blueprint, thing.Blueprint.transform);
                return;
            }

            if (blueprintContainer == null)
            {
                blueprintContainer = new GameObject("~Blueprints");
                UnityEngine.Object.DontDestroyOnLoad(blueprintContainer);
                blueprintContainer.SetActive(false);
            }

            var blueprint = new GameObject(thing.PrefabName);
            blueprint.transform.parent = blueprintContainer.transform;
            blueprint.AddComponent<MeshFilter>();
            blueprint.AddComponent<MeshRenderer>();
            blueprint.AddComponent<Wireframe>();

            GenerateWireframe(blueprint, thing.transform);

            thing.Blueprint = blueprint;
        }

        private static void GenerateWireframe(GameObject blueprint, Transform srcTransform)
        {
            var wireframe = blueprint.GetComponent<Wireframe>();
            var meshFilter = blueprint.GetComponent<MeshFilter>();
            var meshRenderer = blueprint.GetComponent<MeshRenderer>();
            meshRenderer.materials = StationeersModsUtility.GetBlueprintMaterials(1);

            if (wireframe == null || wireframe.WireframeEdges.Count > 0)
            {
                return;
            }

            Debug.LogWarning($"generating missing blueprint {blueprint.name}");

            var gen = new WireframeGenerator(srcTransform);

            meshFilter.mesh = gen.CombinedMesh;
            meshRenderer.materials = StationeersModsUtility.GetBlueprintMaterials(1);
            wireframe.WireframeEdges = gen.Edges;
            wireframe.ShowTransformArrow = false;
        }
    }
}
