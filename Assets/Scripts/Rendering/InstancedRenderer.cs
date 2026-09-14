using UnityEngine;
using Unity.Collections;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
public class InstancedRenderer : MonoBehaviour
{
    private const int RenderBatchSize = 500;

    private RenderParams renderParams;
    private Mesh mesh;
    [SerializeField]
    private Color fallbackColor = Color.white;
    [SerializeField]
    private float fallbackMeshScale = 1f;

    private void Awake()
    {
        MeshFilter meshSource = GetComponent<MeshFilter>();
        MeshRenderer materialSource = GetComponent<MeshRenderer>();

        // Use the assignmed model when it exists (GitHub clones will have to use cubes bc licensed models not uploaded)
        if (meshSource.sharedMesh != null && materialSource.sharedMaterial != null)
        {
            mesh = meshSource.sharedMesh;

            renderParams = new RenderParams( materialSource.sharedMaterial );

            materialSource.sharedMaterial.enableInstancing = true;

            return;
        }

        // Fallback for GitHub clones that do not contain licensed models
        GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);

        mesh = Instantiate(cube.GetComponent<MeshFilter>().sharedMesh);

        Vector3[] vertices = mesh.vertices;

        for (int i = 0; i < vertices.Length; i++)
            vertices[i] *= fallbackMeshScale;

        mesh.vertices = vertices;
        mesh.RecalculateBounds();

        Material fallbackMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));

        // Cube colors will be Sheep: White, Wolf: Red, Dog: Blue
        fallbackMaterial.color = fallbackColor;
        fallbackMaterial.enableInstancing = true;

        renderParams = new RenderParams(fallbackMaterial);

        GameObject plane = GameObject.Find("Plane");

        // Make plane green bc 3rd party texture won't be included in clone
        if (plane != null)
        {
            MeshRenderer planeRenderer = plane.GetComponent<MeshRenderer>();

            Material groundFallback = new Material(Shader.Find("Universal Render Pipeline/Lit"));

            groundFallback.color = new Color(109f / 255f, 143f / 255f, 103f / 255f);

            planeRenderer.material = groundFallback;
        }

        Destroy(cube);
    }

    public void Render(Matrix4x4[] matrices, int count)
    {
        for (int start = 0; start < count; start += RenderBatchSize)
        {
            int batchCount = Mathf.Min( RenderBatchSize, count - start );

            Graphics.RenderMeshInstanced( renderParams, mesh, 0, matrices, batchCount, start );
        }
    }

    public void Render(NativeArray<Matrix4x4> matrices, int count)
    {
        for (int start = 0; start < count; start += RenderBatchSize)
        {
            int batchCount = Mathf.Min( RenderBatchSize, count - start );

            Graphics.RenderMeshInstanced( renderParams, mesh, 0, matrices, batchCount, start );
        }
    }
}