using UnityEngine;
using Unity.Mathematics;
using UnityEngine.UI;

public class CameraViewController : MonoBehaviour
{
    private enum CameraView
    {
        Overview,
        Sheep,
        Wolf,
        Dog
    }

    private Camera mainCamera;
    private SheepSimulationSystem sheepSystem;
    private WolfSystem wolfSystem;
    private DogSystem dogSystem;

    // Camera buttons
    public Button overviewButton;
    public Button sheepButton;
    public Button wolfButton;
    public Button dogButton;

    private Color overviewButtonColor;
    private Color sheepButtonColor;
    private Color wolfButtonColor;
    private Color dogButtonColor;

    private CameraView currentView = CameraView.Overview;

    // True while a camera button is controlling the camera.
    // Any manual camera movement unlocks it
    private bool cameraLocked = true;

    // Freelook attributes
    private float freeMoveSpeed;
    private float freeLookSpeed = 2f;
    private float freeZoomSpeed;

    // Reference to entity we're tracking
    private int currentSheep = -1;
    private int currentWolf = -1;
    private int currentDog = -1;

    // Remember the camera's original overview position
    private Vector3 overviewPosition;
    private Quaternion overviewRotation;

    // Position of tracking camera relative to entity
    private Vector3 trackingOffset = new Vector3(0f, 15f, -15f);

    /*
        Loads necessary components and sets camera to default position
    */
    private void Start()
    {
        mainCamera = Camera.main;

        // Entity systems
        sheepSystem = FindFirstObjectByType<SheepSimulationSystem>();
        wolfSystem = FindFirstObjectByType<WolfSystem>();
        dogSystem = FindFirstObjectByType<DogSystem>();

        // Move the overview camera farther away for larger pastures
        overviewPosition = new Vector3( 0f, SimulationConfig.PastureSize * 0.75f, -SimulationConfig.PastureSize * 0.75f );
        overviewRotation = Quaternion.Euler(45f, 0f, 0f);
        mainCamera.transform.position = overviewPosition;
        mainCamera.transform.rotation = overviewRotation;

        // Large pastures require a farther clipping distance
        mainCamera.farClipPlane = SimulationConfig.PastureSize * 4f;

        overviewButtonColor = overviewButton.image.color;
        sheepButtonColor = sheepButton.image.color;
        wolfButtonColor = wolfButton.image.color;
        dogButtonColor = dogButton.image.color;

        overviewButton.onClick.AddListener(ShowOverview);
        sheepButton.onClick.AddListener(ShowSheep);
        wolfButton.onClick.AddListener(ShowWolf);
        dogButton.onClick.AddListener(ShowDog);

        UpdateCameraButtonColors();
    }

    /*
        Allows for Unity-editor-like camera movement controls
    */
    private void Update()
    {
        float mouseX = Input.GetAxis("Mouse X");
        float mouseY = Input.GetAxis("Mouse Y");
        float scroll = Input.mouseScrollDelta.y;

        bool rightMouseHeld = Input.GetMouseButton(1);

        bool movementKeyHeld =
            Input.GetKey(KeyCode.W) ||
            Input.GetKey(KeyCode.A) ||
            Input.GetKey(KeyCode.S) ||
            Input.GetKey(KeyCode.D) ||
            Input.GetKey(KeyCode.Q) ||
            Input.GetKey(KeyCode.E);

        // Any manual camera input releases the current camera lock
        if (movementKeyHeld || rightMouseHeld || scroll != 0f)
            cameraLocked = false;

        // A camera button is still controlling the camera
        if (cameraLocked)
            return;

        // Camera moves faster when high above this height, but slows back down
        // as the camera approaches ground level
        const float NormalSpeedHeight = 100f;

        float heightRatio = mainCamera.transform.position.y / NormalSpeedHeight;
        float heightSpeedScale = Mathf.Clamp( heightRatio * heightRatio, 1f, 20f );
        freeMoveSpeed = 50f * heightSpeedScale;
        freeZoomSpeed = 100f * heightSpeedScale;

        // WASD movement.
        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");

        Vector3 movement = mainCamera.transform.right * horizontal + mainCamera.transform.forward * vertical;

        // E = up, Q = down.
        if (Input.GetKey(KeyCode.E))
            movement += Vector3.up;

        if (Input.GetKey(KeyCode.Q))
            movement -= Vector3.up;

        mainCamera.transform.position += movement * freeMoveSpeed * Time.deltaTime;

        // Hold right mouse button and drag to look around
        if (rightMouseHeld)
        {
            mainCamera.transform.Rotate( Vector3.up, mouseX * freeLookSpeed, Space.World );

            mainCamera.transform.Rotate( Vector3.right, -mouseY * freeLookSpeed, Space.Self );
        }

        // Mouse wheel moves forward/back
        mainCamera.transform.position += mainCamera.transform.forward * scroll * freeZoomSpeed * Time.deltaTime;
    }

    // Camera movement - set to default Overview, or follows an entity
    private void LateUpdate()
    {
        if (!cameraLocked)
            return;

        // Overview uses the camera's original position
        if (currentView == CameraView.Overview)
        {
            mainCamera.transform.position = overviewPosition;
            mainCamera.transform.rotation = overviewRotation;

            return;
        }

        // Follow Sheep
        if (currentView == CameraView.Sheep)
        {
            // If the sheep was caught, automatically choose another one
            if ( currentSheep == -1 || sheepSystem.Current[currentSheep].lost )
                PickRandomSheep();

            if (currentSheep != -1)
                FollowPosition( sheepSystem.Current[currentSheep].position );
        }

        // Follow Wolf
        if (currentView == CameraView.Wolf)
        {
            // If the wolf was caught, automatically choose another one
            if ( currentWolf == -1 || wolfSystem.States[currentWolf].lost != 0 )
                PickRandomWolf();

            if (currentWolf != -1)
                FollowPosition( wolfSystem.States[currentWolf].position );
        }

        // Follow Dog
        if (currentView == CameraView.Dog)
        {
            if (currentDog != -1)
                FollowPosition( dogSystem.States[currentDog].position );
        }
    }

    /*
        // Tells camera to follow and look at the given coordinates
    */
    private void FollowPosition(float2 position)
    {
        Vector3 targetPosition = new Vector3( position.x, 0f, position.y );
        mainCamera.transform.position = targetPosition + trackingOffset;
        mainCamera.transform.LookAt( targetPosition );
    }

    // Pick a random Sheep for the camera to follow
    private void PickRandomSheep()
    {
        int previousSheep = currentSheep;

        // Count living sheep other than the one already selected
        int availableSheep = 0;

        for (int i = 0; i < sheepSystem.Current.Length; i++)
        {
            if ( !sheepSystem.Current[i].lost && i != previousSheep )
                availableSheep++;
        }

        // No different living sheep exists
        if (availableSheep == 0)
        {
            // Keep current sheep if it is still alive
            if ( previousSheep != -1 && !sheepSystem.Current[previousSheep].lost )
                return;

            currentSheep = -1;
            return;
        }

        int randomChoice =
            UnityEngine.Random.Range(0, availableSheep);

        for (int i = 0; i < sheepSystem.Current.Length; i++)
        {
            if ( !sheepSystem.Current[i].lost && i != previousSheep )
            {
                if (randomChoice == 0)
                {
                    currentSheep = i;
                    return;
                }

                randomChoice--;
            }
        }
    }

    // Pick a random Wolf for the camera to follow
    private void PickRandomWolf()
    {
        int previousWolf = currentWolf;

        // Count living wolves other than the one already selected
        int availableWolves = 0;

        for (int i = 0; i < wolfSystem.States.Length; i++)
        {
            if ( wolfSystem.States[i].lost == 0 && i != previousWolf )
                availableWolves++;
        }

        // No different living wolf exists
        if (availableWolves == 0)
            return;

        // Pick one of the available wolves
        int randomChoice =
            UnityEngine.Random.Range(0, availableWolves);

        for (int i = 0; i < wolfSystem.States.Length; i++)
        {
            if ( wolfSystem.States[i].lost == 0 && i != previousWolf )
            {
                if (randomChoice == 0)
                {
                    currentWolf = i;
                    return;
                }

                randomChoice--;
            }
        }
    }


    // Pick a random Dog for the camera to follow
    private void PickRandomDog()
    {
        int previousDog = currentDog;

        // Only one dog means there is no different dog to choose
        if (dogSystem.Count <= 1)
        {
            currentDog = 0;
            return;
        }

        currentDog = previousDog;

        while (currentDog == previousDog)
            currentDog = UnityEngine.Random.Range( 0, dogSystem.Count );
    }

    private void ShowOverview()
    {
        SetCameraView(CameraView.Overview);
    }

    private void ShowSheep()
    {
        SetCameraView(CameraView.Sheep);
    }

    private void ShowWolf()
    {
        SetCameraView(CameraView.Wolf);
    }

    private void ShowDog()
    {
        SetCameraView(CameraView.Dog);
    }

    private void SetCameraView(CameraView view)
    {
        currentView = view;
        cameraLocked = true;

        if (view == CameraView.Sheep)
            PickRandomSheep();

        if (view == CameraView.Wolf)
            PickRandomWolf();

        if (view == CameraView.Dog)
            PickRandomDog();

        UpdateCameraButtonColors();
    }

    private void UpdateCameraButtonColors()
    {
        SetButtonColor(
            overviewButton,
            overviewButtonColor,
            currentView == CameraView.Overview
        );

        SetButtonColor(
            sheepButton,
            sheepButtonColor,
            currentView == CameraView.Sheep
        );

        SetButtonColor(
            wolfButton,
            wolfButtonColor,
            currentView == CameraView.Wolf
        );

        SetButtonColor(
            dogButton,
            dogButtonColor,
            currentView == CameraView.Dog
        );
    }

    private void SetButtonColor(Button button, Color normalColor, bool active)
    {
        if (active)
            button.image.color = Color.green;
        else
            button.image.color = normalColor;
    }
}