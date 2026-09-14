using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class BakeSheepMesh
{
    [MenuItem("Tools/Bake Selected Model To Static Mesh")]
    private static void BakeSelectedSheep()
    {
        GameObject root = Selection.activeGameObject;

        if (root == null)
        {
            Debug.LogError("Select the sheep prefab instance in the Hierarchy first.");
            return;
        }

        SkinnedMeshRenderer[] renderers =
            root.GetComponentsInChildren<SkinnedMeshRenderer>();

        if (renderers.Length == 0)
        {
            Debug.LogError("No SkinnedMeshRenderers found under the selected object.");
            return;
        }

        List<CombineInstance> combineInstances =
            new List<CombineInstance>();

        List<Mesh> temporaryMeshes =
            new List<Mesh>();

        foreach (SkinnedMeshRenderer renderer in renderers)
        {
            Mesh bakedMesh = new Mesh();

            // Take a snapshot of this animated/skinned mesh piece.
            renderer.BakeMesh(bakedMesh, false);

            temporaryMeshes.Add(bakedMesh);

            CombineInstance combine = new CombineInstance
            {
                mesh = bakedMesh,

                // Convert this piece into the selected sheep root's local space.
                transform =
                    root.transform.worldToLocalMatrix *
                    renderer.transform.localToWorldMatrix
            };

            combineInstances.Add(combine);
        }

        Mesh combinedMesh = new Mesh
        {
            name = root.name + "Static",
            indexFormat = IndexFormat.UInt32
        };

        combinedMesh.CombineMeshes(
            combineInstances.ToArray(),
            true,
            true
        );

        combinedMesh.RecalculateBounds();

        string path =
            AssetDatabase.GenerateUniqueAssetPath(
                "Assets/" + root.name + "Static.asset"
            );

        AssetDatabase.CreateAsset(combinedMesh, path);
        AssetDatabase.SaveAssets();

        foreach (Mesh mesh in temporaryMeshes)
        {
            Object.DestroyImmediate(mesh);
        }

        Selection.activeObject = combinedMesh;

        Debug.Log(
            "Created static sheep mesh: " + path
        );
    }
}