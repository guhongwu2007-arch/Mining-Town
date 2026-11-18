using System;
using System.Collections.Generic;
using UnityEngine;

namespace MiningTown
{
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(SaveManager))]
    [DisallowMultipleComponent]
    public sealed class MiningTownPrototype : MonoBehaviour
    {
        public Material worldMaterial;
        [SerializeField, Range(1f, 2f)] private float roadSpeedMultiplier = 1.2f;
        [Tooltip("Extra flat road cubes, for paths using a different material.")]
        [SerializeField] private List<MeshRenderer> extraRoads = new List<MeshRenderer>();
        [SerializeField, HideInInspector] private Material roadMaterial;
        private readonly List<MeshRenderer> roads = new List<MeshRenderer>();
        private SaveManager saves;
        [SerializeField, HideInInspector] private bool editable;
        [SerializeField, HideInInspector] private Transform player;
        [SerializeField, HideInInspector] private Transform ground, river, bridge, mineCliff;
        public bool IsEditableScene { get { return editable; } }
        [SerializeField, HideInInspector] private Camera sceneCamera;
        private readonly List<Material> materials = new List<Material>();
        [SerializeField, HideInInspector] private List<GameObject> bundles = new List<GameObject>();
        [SerializeField, HideInInspector] private List<HouseView> houses = new List<HouseView>();
        [SerializeField, HideInInspector] private GameObject brokenBridge, fixedBridge;
        private Material timber, slate, gold, cream, foliage, stone, damagedWall;
        private GUIStyle titleStyle, headingStyle, textStyle, smallStyle, buttonStyle, tagStyle;
        private Font uiFont;
        private bool menuOpen;
        private Vector2 pointer;
        private bool pointerReleased;
        private Action pendingAction;
        private string pendingTitle;
        private static readonly Vector3 CameraOffset = new Vector3(0, 18f, -15f);

        [Serializable]
        private sealed class HouseView
        {
            public string Id;
            public Transform Root;
            public Material DamagedWall;
            [NonSerialized] public BuildingDefinition Definition;
            public Renderer Wall;
            public Material RestoredWall;
            public GameObject Broken, Restored;
        }

        private struct Target
        {
            public string Kind, Id;
            public int NodeId;
            public string Prompt;
        }

        private void Awake()
        {
            WorldLayout.ResetLayout();
            if (!editable) return;
            WorldLayout.MinX = ground.position.x - ground.lossyScale.x / 2 + 1;
            WorldLayout.MaxX = ground.position.x + ground.lossyScale.x / 2 - 1;
            WorldLayout.MinZ = ground.position.z - ground.lossyScale.z / 2 + 1;
            WorldLayout.MaxZ = ground.position.z + ground.lossyScale.z / 2 - 1;
            WorldLayout.RiverSouth = river.position.z - river.lossyScale.z / 2;
            WorldLayout.RiverNorth = river.position.z + river.lossyScale.z / 2;
            WorldLayout.BridgeX = bridge.position.x;
            WorldLayout.BridgeApproachZ = WorldLayout.RiverSouth - 1.5f;
            WorldLayout.BridgeHalfWidth = 1.5f * bridge.lossyScale.x;
            WorldLayout.MineX = mineCliff.position.x;
            WorldLayout.MineFront = mineCliff.position.z - mineCliff.lossyScale.z / 2 - 0.4f;
            WorldLayout.MineBack = mineCliff.position.z + mineCliff.lossyScale.z / 2 + 0.4f;
            WorldLayout.MineHalfWidth = mineCliff.lossyScale.x / 2 + 0.4f;
            WorldLayout.MineDoorZ = WorldLayout.MineFront - 1.4f;
            foreach (var house in houses)
            {
                BuildingDefinition original = WorldLayout.FindBuilding(house.Id);
                var t = house.Root;
                var definition = new BuildingDefinition(original.Id, original.Name, t.position.x,
                    t.position.z, original.WoodCost, original.CanRepair);
                definition.SetFootprint(t.right.x, t.right.z, t.forward.x, t.forward.z,
                    BuildingDefinition.Width * t.lossyScale.x, BuildingDefinition.Depth * t.lossyScale.z);
                int slot = Array.IndexOf(WorldLayout.Buildings, original);
                WorldLayout.Buildings[slot] = definition;
                house.Definition = definition;
                house.DamagedWall = house.Wall.sharedMaterial;
            }
            for (int i = 0; i < bundles.Count; i++)
            {
                Vector3 position = bundles[i].transform.position;
                WorldLayout.Resources[i] = new ResourcePoint(position.x, position.z);
            }
            WorldLayout.SpawnX = player.position.x; WorldLayout.SpawnZ = player.position.z;

        }

#if UNITY_EDITOR
        public void BakeForScene(Action<Material> persistMaterial)
        {
            if (Application.isPlaying || editable) throw new InvalidOperationException("Convert the scene once in Edit mode.");
            WorldLayout.ResetLayout();
            BuildWorld();
            foreach (var material in materials) persistMaterial(material);
            materials.Clear();
            player.position = new Vector3(0, 1, -4);
            SnapCamera();
            foreach (var house in houses) house.Restored.SetActive(false);
            fixedBridge.SetActive(false);
            editable = true;
        }
#endif

        private void Start()
        {
            saves = GetComponent<SaveManager>();
            if (!editable) BuildWorld();
            FindRoads();
            saves.StateChanged += RefreshWorld;
            RefreshWorld();
            SnapCamera();
        }

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            Vector2 screenPoint;
            pointerReleased = PrototypeInput.PointerReleased(out screenPoint);
            pointer = new Vector2(screenPoint.x * 1280f / Screen.width,
                (Screen.height - screenPoint.y) * 720f / Screen.height);
#endif
            if (player == null) return;
            if (PrototypeInput.MenuPressed())
            {
                menuOpen = !menuOpen;
                pendingAction = null;
                return;
            }
            // Menu actions remain available, but the player cannot move or spend resources behind it.
            if (menuOpen || pendingAction != null) return;
            Vector2 input = PrototypeInput.Movement();
            if (input.sqrMagnitude > 0f)
            {
                float multiplier = IsOnRoad(player.position) ? roadSpeedMultiplier : 1f;
                float distance = 5f * multiplier * Mathf.Min(Time.deltaTime, 0.05f);
                // Each move goes through the normal wall, river and bridge checks.
                int steps = Mathf.Max(1, Mathf.CeilToInt(distance / 0.3f));
                float step = distance / steps;
                for (int i = 0; i < steps; i++)
                    saves.MovePlayer(input.x * step, input.y * step);
            }
            player.position = StatePosition();
            if (PrototypeInput.InteractPressed()) Interact();
        }

        private static bool IsOriginalRoad(string objectName)
        {
            switch (objectName)
            {
                case "Main street":
                case "South lane":
                case "Southern houses lane":
                case "Trail to bridge":
                case "Trail to mine": return true;
                default: return false;
            }
        }

        private void FindRoads()
        {
            var renderers = GetComponentsInChildren<MeshRenderer>(true);
            var trailMaterials = new HashSet<Material>();
            if (roadMaterial != null) trailMaterials.Add(roadMaterial);
            foreach (var item in renderers)
                if (IsOriginalRoad(item.name) && item.sharedMaterial != null)
                    trailMaterials.Add(item.sharedMaterial);
            roads.Clear();
            foreach (var item in renderers)
                if (IsOriginalRoad(item.name) || trailMaterials.Contains(item.sharedMaterial))
                    roads.Add(item);
            foreach (var item in extraRoads)
                if (item != null && !roads.Contains(item)) roads.Add(item);
        }

        private bool IsOnRoad(Vector3 position)
        {
            foreach (var road in roads)
            {
                if (road == null || !road.enabled || !road.gameObject.activeInHierarchy) continue;
                var local = road.transform.InverseTransformPoint(position);
                var bounds = road.localBounds;
                if (local.x >= bounds.min.x && local.x <= bounds.max.x
                    && local.z >= bounds.min.z && local.z <= bounds.max.z) return true;
            }
            return false;
        }

        private void LateUpdate()
        {
            if (sceneCamera == null || player == null) return;
            Vector3 target = new Vector3(player.position.x, 0, player.position.z) + CameraOffset;
            sceneCamera.transform.position = Vector3.Lerp(sceneCamera.transform.position, target,
                1f - Mathf.Exp(-9f * Time.deltaTime));
        }

        private Vector3 StatePosition()
        { return new Vector3(saves.State.playerX, saves.State.playerY, saves.State.playerZ); }

        private void SnapCamera()
        {
            if (sceneCamera != null)
                sceneCamera.transform.position = new Vector3(player.position.x, 0, player.position.z) + CameraOffset;
        }

        private float Distance(float x, float z)
        { return Vector2.Distance(new Vector2(player.position.x, player.position.z), new Vector2(x, z)); }

        private Target NearestTarget()
        {
            Target target = new Target { Kind = "", Prompt = "Explore the town. The bridge is to the north-east." };
            float best = 2.1f;
            for (int i = 0; i < WorldLayout.Resources.Length; i++)
            {
                if (saves.State.collectedWoodIds.Contains(i)) continue;
                ResourcePoint p = WorldLayout.Resources[i];
                float distance = Distance(p.X, p.Z);
                if (distance < best)
                {
                    best = distance;
                    target = new Target { Kind = "wood", NodeId = i, Prompt = "[E] Collect wood (+2)" };
                }
            }
            foreach (BuildingDefinition house in WorldLayout.Buildings)
            {
                float distance = Distance(house.DoorX, house.DoorZ);
                if (distance >= best) continue;
                best = distance;
                string prompt = !house.CanRepair ? "[E] Inspect the abandoned ruins"
                    : saves.State.repairedBuildingIds.Contains(house.Id) ? house.Name + " is restored."
                    : "[E] Repair " + house.Name + " (" + house.WoodCost + " wood / +20 Honor)";
                target = new Target { Kind = "house", Id = house.Id, Prompt = prompt };
            }
            float bridgeDistance = Mathf.Min(Distance(WorldLayout.BridgeX, WorldLayout.BridgeApproachZ),
                Distance(WorldLayout.BridgeX, WorldLayout.RiverNorth + 1.5f));
            if (bridgeDistance < best)
            {
                best = bridgeDistance;
                target = new Target { Kind = "bridge", Prompt = saves.State.bridgeRepaired
                    ? "Bridge restored. You can cross the river." : "[E] Repair bridge (12 wood / +20 Honor)" };
            }
            if (Distance(WorldLayout.MineX, WorldLayout.MineDoorZ) < best)
                target = new Target { Kind = "mine", Prompt = "[E] Inspect the sealed mine entrance" };
            return target;
        }

        private void Interact()
        {
            if (menuOpen || pendingAction != null) return;
            Target target = NearestTarget();
            switch (target.Kind)
            {
                case "wood": saves.Collect(target.NodeId); break;
                case "house": saves.RepairBuilding(target.Id); break;
                case "bridge": saves.RepairBridge(); break;
                case "mine": saves.ShowMineMessage(); break;
            }
        }

        private void RefreshWorld()
        {
            bool teleport = Vector3.Distance(player.position, StatePosition()) > 3f;
            player.position = StatePosition();
            for (int i = 0; i < bundles.Count; i++)
                bundles[i].SetActive(!saves.State.collectedWoodIds.Contains(i));
            foreach (HouseView house in houses)
            {
                bool repaired = saves.State.repairedBuildingIds.Contains(house.Definition.Id);
                house.Wall.sharedMaterial = repaired ? house.RestoredWall : house.DamagedWall;
                house.Broken.SetActive(!repaired);
                house.Restored.SetActive(repaired);
            }
            brokenBridge.SetActive(!saves.State.bridgeRepaired);
            fixedBridge.SetActive(saves.State.bridgeRepaired);
            if (teleport) SnapCamera();
        }

        private void BuildWorld()
        {
            timber = Colored(new Color(0.43f, 0.27f, 0.16f));
            slate = Colored(new Color(0.24f, 0.31f, 0.34f));
            gold = Colored(new Color(0.96f, 0.71f, 0.28f));
            cream = Colored(new Color(0.94f, 0.88f, 0.71f));
            foliage = Colored(new Color(0.21f, 0.35f, 0.29f));
            stone = Colored(new Color(0.38f, 0.41f, 0.39f));
            damagedWall = Colored(new Color(0.43f, 0.45f, 0.40f));
            Material grass = Colored(new Color(0.34f, 0.43f, 0.35f));
            Material trail = Colored(new Color(0.61f, 0.53f, 0.39f));
            roadMaterial = trail;
            Material water = Colored(new Color(0.20f, 0.42f, 0.49f));
            Material ripples = Colored(new Color(0.34f, 0.56f, 0.58f));
            ground = Shape("Town and mine grounds", PrimitiveType.Cube, new Vector3(0, -0.2f, 9.5f), new Vector3(48, 0.3f, 57), grass).transform;
            Shape("Main street", PrimitiveType.Cube, new Vector3(0, -0.03f, -1), new Vector3(44, 0.06f, 2), trail);
            Shape("South lane", PrimitiveType.Cube, new Vector3(0, -0.02f, -8.5f), new Vector3(2, 0.07f, 16), trail);
            Shape("Southern houses lane", PrimitiveType.Cube, new Vector3(0, -0.02f, -12.5f), new Vector3(28, 0.07f, 1.7f), trail);
            Shape("Trail to bridge", PrimitiveType.Cube, new Vector3(14, -0.02f, 8), new Vector3(2.4f, 0.07f, 20), trail);
            Shape("Trail to mine", PrimitiveType.Cube, new Vector3(14, -0.02f, 26), new Vector3(2.4f, 0.07f, 6), trail);
            var crossing = Group("River Crossing").transform;
            crossing.position = new Vector3(0, 0, 20.5f);
            river = Shape("River", PrimitiveType.Cube, new Vector3(0, 0.015f, 20.5f), new Vector3(48, 0.04f, 5), water).transform;
            river.SetParent(crossing, true);
            for (int i = 0; i < 16; i++)
                Shape("Water ripple", PrimitiveType.Cube, new Vector3(-22f + i * 2.9f, 0.045f, 19.2f + (i % 3) * 1.1f), new Vector3(1.2f, 0.012f, 0.08f), ripples).transform.SetParent(crossing, true);
            for (int i = 0; i < WorldLayout.Buildings.Length; i++) BuildHouse(WorldLayout.Buildings[i], i);
            bridge = Group("Bridge").transform;
            bridge.position = new Vector3(WorldLayout.BridgeX, 0, 20.5f);
            bridge.SetParent(crossing, true);
            BuildBridge();
            brokenBridge.transform.SetParent(bridge, true);
            fixedBridge.transform.SetParent(bridge, true);
            Transform mine = Group("Sealed Mine").transform;
            mine.position = new Vector3(WorldLayout.MineX, 0, 33);
            int firstMinePart = transform.childCount;
            BuildMine();
            while (transform.childCount > firstMinePart)
                transform.GetChild(firstMinePart).SetParent(mine, true);
            Transform woodGroup = Group("Wood Bundles").transform;
            BuildResources();
            foreach (var bundle in bundles) bundle.transform.SetParent(woodGroup, true);
            Transform trees = Group("Boundary Trees").transform;
            int firstTreePart = transform.childCount;
            for (int z = -17; z < 38; z += 4)
            {
                if (z >= 17 && z <= 24) continue;
                BuildTree(-24f, z); BuildTree(24f, z + 1);
            }
            for (int x = -21; x <= 21; x += 4) { BuildTree(x, -19f); BuildTree(x, 38f); }

            while (transform.childCount > firstTreePart)
                transform.GetChild(firstTreePart).SetParent(trees, true);

            GameObject miner = Group("Player");
            player = miner.transform;
            Shape("Miner body", PrimitiveType.Capsule, Vector3.zero, new Vector3(0.65f, 0.65f, 0.65f),
                Colored(new Color(0.27f, 0.48f, 0.57f)), player);
            Shape("Hard hat", PrimitiveType.Sphere, new Vector3(0, 0.58f, 0), new Vector3(0.76f, 0.3f, 0.76f), gold, player);
            Shape("Hat lamp", PrimitiveType.Sphere, new Vector3(0, 0.6f, -0.35f), Vector3.one * 0.18f, cream, player);

            GameObject cameraObject = Group("Mining Town Camera");
            sceneCamera = cameraObject.AddComponent<Camera>();
            sceneCamera.tag = "MainCamera";
            sceneCamera.orthographic = true;
            sceneCamera.orthographicSize = 9f;
            sceneCamera.nearClipPlane = 0.1f; sceneCamera.farClipPlane = 150f;
            sceneCamera.rect = new Rect(0, 0, 1, 1);
            sceneCamera.clearFlags = CameraClearFlags.SolidColor;
            sceneCamera.backgroundColor = new Color(0.14f, 0.22f, 0.21f);
            cameraObject.transform.rotation = Quaternion.LookRotation(-CameraOffset, Vector3.up);
            cameraObject.AddComponent<AudioListener>();
        }

        private void BuildTree(float x, float z)
        {
            Shape("Tree trunk", PrimitiveType.Cylinder, new Vector3(x, 0.65f, z), new Vector3(0.3f, 0.65f, 0.3f), timber);
            Shape("Tree canopy", PrimitiveType.Sphere, new Vector3(x, 2.1f, z), new Vector3(2, 2.5f, 2), foliage);
        }

        private void BuildHouse(BuildingDefinition definition, int index)
        {
            GameObject root = Group(definition.Name);
            root.transform.localPosition = new Vector3(definition.X, 0, definition.Z);
            GameObject damaged = Group("Unrepaired details", root.transform);
            GameObject restored = Group("Restored details", root.transform);
            float height = definition.CanRepair ? 2.4f : 1.2f;
            Renderer wall = Shape("Walls", PrimitiveType.Cube, new Vector3(0, height / 2f, 0),
                new Vector3(BuildingDefinition.Width, height, BuildingDefinition.Depth), damagedWall, root.transform).GetComponent<Renderer>();
            Material warmWall = Colored(new Color(0.72f + index * 0.025f, 0.59f + index * 0.018f, 0.42f));
            houses.Add(new HouseView { Definition = definition, Id = definition.Id, Root = root.transform, DamagedWall = damagedWall, Wall = wall, RestoredWall = warmWall,
                Broken = damaged, Restored = restored });
            Shape("Porch", PrimitiveType.Cube, new Vector3(0, 0.08f, -1.85f), new Vector3(2.7f, 0.15f, 0.9f), timber, root.transform);
            if (!definition.CanRepair)
            {
                GameObject fallenRoof = Shape("Collapsed roof", PrimitiveType.Cube, new Vector3(-0.4f, 1.35f, 0.2f), new Vector3(2, 0.2f, 2.5f), slate, damaged.transform);
                fallenRoof.transform.localRotation = Quaternion.Euler(10, 12, 24);
                Shape("Broken chimney", PrimitiveType.Cube, new Vector3(0.9f, 1.5f, 0.7f), new Vector3(0.5f, 1.6f, 0.5f), stone, damaged.transform);
                for (int i = 0; i < 4; i++)
                    Shape("Rubble", PrimitiveType.Cube, new Vector3(-1f + i * 0.65f, 0.35f, -1.2f), new Vector3(0.45f, 0.7f, 0.5f), stone, damaged.transform);
                return;
            }
            Shape("Door", PrimitiveType.Cube, new Vector3(0, 0.8f, -1.39f), new Vector3(0.65f, 1.6f, 0.1f), timber, root.transform);
            for (int side = -1; side <= 1; side += 2)
            {
                GameObject roof = Shape("Roof slope", PrimitiveType.Cube, new Vector3(side * 0.8f, 2.7f, 0), new Vector3(2, 0.2f, 3.1f), slate, root.transform);
                roof.transform.localRotation = Quaternion.Euler(0, 0, -side * 26);
                Shape("Window", PrimitiveType.Cube, new Vector3(side * 0.95f, 1.4f, -1.4f), new Vector3(0.55f, 0.7f, 0.12f), gold, restored.transform);
                for (int board = 0; board < 2; board++)
                {
                    GameObject plank = Shape("Boarded window", PrimitiveType.Cube, new Vector3(side * 0.95f, 1.15f + board * 0.4f, -1.44f), new Vector3(0.85f, 0.18f, 0.16f), timber, damaged.transform);
                    plank.transform.localRotation = Quaternion.Euler(0, 0, side * 12);
                }
            }
            Shape("Chimney", PrimitiveType.Cube, new Vector3(0.8f, 3.1f, 0.5f), new Vector3(0.45f, 1, 0.5f), stone, root.transform);
            Shape("Front trim", PrimitiveType.Cube, new Vector3(0, 2.2f, -1.42f), new Vector3(3, 0.14f, 0.12f), cream, restored.transform);
        }

        private void BuildResources()
        {
            for (int i = 0; i < WorldLayout.Resources.Length; i++)
            {
                ResourcePoint point = WorldLayout.Resources[i];
                GameObject bundle = Group("Wood bundle " + i);
                bundle.transform.localPosition = new Vector3(point.X, 0, point.Z);
                bundles.Add(bundle);
                for (int log = 0; log < 3; log++)
                {
                    GameObject logObject = Shape("Log", PrimitiveType.Cylinder, new Vector3(0, 0.22f, (log - 1) * 0.3f), new Vector3(0.28f, 0.65f, 0.28f), timber, bundle.transform);
                    logObject.transform.localRotation = Quaternion.Euler(0, 0, 90);
                }
                Shape("Bundle marker", PrimitiveType.Cylinder, new Vector3(0, 0.02f, 0), new Vector3(1.75f, 0.025f, 1.25f), gold, bundle.transform);
            }
        }

        private void BuildBridge()
        {
            brokenBridge = Group("Broken bridge");
            fixedBridge = Group("Restored bridge");
            for (int i = 0; i <= 12; i++)
            {
                float z = 17.8f + i * 0.45f;
                Shape("Bridge plank", PrimitiveType.Cube, new Vector3(WorldLayout.BridgeX, 0.12f, z), new Vector3(3.8f, 0.18f, 0.41f), timber, fixedBridge.transform);
                if (i < 2 || i > 10)
                {
                    GameObject piece = Shape("Broken deck", PrimitiveType.Cube, new Vector3(WorldLayout.BridgeX, 0.1f, z), new Vector3(3.4f, 0.15f, 0.35f), timber, brokenBridge.transform);
                    piece.transform.localRotation = Quaternion.Euler(0, (i % 2 == 0 ? 8 : -8), 0);
                }
            }
            for (int side = -1; side <= 1; side += 2)
            {
                float x = WorldLayout.BridgeX + side * 1.85f;
                for (int i = 0; i < 4; i++)
                    Shape("Bridge post", PrimitiveType.Cube, new Vector3(x, 0.65f, 18f + i * 1.7f), new Vector3(0.18f, 1.3f, 0.18f), timber, fixedBridge.transform);
                Shape("Bridge rail", PrimitiveType.Cube, new Vector3(x, 1.15f, 20.5f), new Vector3(0.15f, 0.15f, 5.4f), cream, fixedBridge.transform);
            }
            Shape("Repair barricade", PrimitiveType.Cube, new Vector3(WorldLayout.BridgeX, 0.8f, 17.85f), new Vector3(3.3f, 0.25f, 0.15f), gold, brokenBridge.transform);
        }

        private void BuildMine()
        {
            Material darkness = Colored(new Color(0.045f, 0.065f, 0.065f));
            mineCliff = Shape("Rock cliff", PrimitiveType.Cube, new Vector3(WorldLayout.MineX, 2.1f, 33), new Vector3(6.6f, 4.2f, 6.4f), stone).transform;
            Shape("Sealed mine opening", PrimitiveType.Cube, new Vector3(WorldLayout.MineX, 1.35f, 29.7f), new Vector3(2.7f, 2.7f, 0.15f), darkness);
            for (int side = -1; side <= 1; side += 2)
                Shape("Mine timber support", PrimitiveType.Cube, new Vector3(WorldLayout.MineX + side * 1.55f, 1.5f, 29.6f), new Vector3(0.3f, 3, 0.35f), timber);
            Shape("Mine lintel", PrimitiveType.Cube, new Vector3(WorldLayout.MineX, 2.9f, 29.6f), new Vector3(3.4f, 0.35f, 0.35f), timber);
            for (int i = 0; i < 3; i++)
            {
                GameObject board = Shape("Sealed entrance board", PrimitiveType.Cube, new Vector3(WorldLayout.MineX, 0.55f + i * 0.7f, 29.5f), new Vector3(3, 0.25f, 0.25f), timber);
                board.transform.localRotation = Quaternion.Euler(0, 0, (i % 2 == 0 ? 8 : -8));
            }
        }

        private GameObject Group(string objectName, Transform parent = null)
        {
            var result = new GameObject(objectName);
            result.transform.SetParent(parent == null ? transform : parent, false);
            return result;
        }

        private Material Colored(Color color)
        {
            Material material;
            if (worldMaterial != null) material = new Material(worldMaterial);
            else
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Unlit/Color");
                if (shader == null) throw new InvalidOperationException("Create the demo with the Mining Town menu to assign its material.");
                material = new Material(shader);
            }
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            materials.Add(material);
            return material;
        }

        private GameObject Shape(string objectName, PrimitiveType type, Vector3 position, Vector3 scale, Material material, Transform parent = null)
        {
            GameObject item = GameObject.CreatePrimitive(type);
            item.name = objectName;
            item.transform.SetParent(parent == null ? transform : parent, false);
            item.transform.localPosition = position;
            item.transform.localScale = scale;
            item.GetComponent<Renderer>().sharedMaterial = material;
            Collider collider = item.GetComponent<Collider>();
            if (collider != null) { collider.enabled = false; if (Application.isPlaying) Destroy(collider); else DestroyImmediate(collider); }
            return item;
        }

        private void Styles()
        {
            if (titleStyle != null) return;
            uiFont = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "Microsoft JhengHei", "Noto Sans CJK SC", "PingFang SC", "Arial Unicode MS", "Arial" }, 16);
            titleStyle = LabelStyle(25, true);
            headingStyle = LabelStyle(17, true);
            textStyle = LabelStyle(16, false);
            smallStyle = LabelStyle(13, false);
            tagStyle = LabelStyle(13, true); tagStyle.alignment = TextAnchor.MiddleCenter;
            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 16, wordWrap = true };
            if (uiFont != null) buttonStyle.font = uiFont;
            titleStyle.normal.textColor = new Color(0.97f, 0.75f, 0.38f);
            smallStyle.normal.textColor = new Color(0.75f, 0.82f, 0.79f);
        }

        private GUIStyle LabelStyle(int size, bool bold)
        {
            var style = new GUIStyle(GUI.skin.label) { fontSize = size, wordWrap = true,
                fontStyle = bold ? FontStyle.Bold : FontStyle.Normal };
            if (uiFont != null) style.font = uiFont;
            style.normal.textColor = new Color(0.93f, 0.94f, 0.90f);
            return style;
        }

        private static void Panel(Rect rect, Color color)
        {
            Color old = GUI.color; GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture); GUI.color = old;
        }

        private bool Button(Rect rect, string caption)
        {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            GUI.Box(rect, caption, buttonStyle);
            if (GUI.enabled && pointerReleased && Event.current.type == EventType.Repaint && rect.Contains(pointer))
            { pointerReleased = false; return true; }
            return false;
#else
            return GUI.Button(rect, caption, buttonStyle);
#endif
        }

        private void OnGUI()
        {
            if (saves == null || player == null) return;
            Styles();
            Matrix4x4 oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity,
                new Vector3(Screen.width / 1280f, Screen.height / 720f, 1));
            DrawWorldLabels();
            DrawHud();
            if (menuOpen) DrawMenu();
            if (pendingAction != null) DrawConfirmation();
            GUI.enabled = true;
            GUI.matrix = oldMatrix;
        }

        private void DrawHud()
        {
            Panel(new Rect(20, 20, 330, 108), new Color(0.07f, 0.13f, 0.15f, 0.96f));
            GUI.Label(new Rect(36, 28, 306, 34), "MINING TOWN", titleStyle);
            GUI.Label(new Rect(36, 68, 305, 25), "WOOD  " + saves.State.wood + "     HONOR  " + saves.State.honor, headingStyle);
            GUI.Label(new Rect(36, 98, 305, 24), "Houses " + saves.State.repairedBuildingIds.Count + "/4    Bridge: "
                + (saves.State.bridgeRepaired ? "OPEN" : "BROKEN"), smallStyle);
            Panel(new Rect(370, 20, 640, 50), new Color(0.07f, 0.13f, 0.15f, 0.94f));
            GUI.Label(new Rect(388, 33, 610, 30), "WASD / Arrows: move    E: interact    M: open / close menu", textStyle);
            GUI.enabled = pendingAction == null;
            if (Button(new Rect(840, 82, 170, 36), menuOpen ? "Close Menu [M]" : "Menu [M]")) menuOpen = !menuOpen;
            DrawMiniMap();
            Panel(new Rect(20, 608, 1240, 92), new Color(0.07f, 0.13f, 0.15f, 0.96f));
            Target target = NearestTarget();
            GUI.Label(new Rect(38, 620, 1035, 29), target.Prompt, headingStyle);
            GUI.enabled = !menuOpen && pendingAction == null;
            if (!String.IsNullOrEmpty(target.Kind) && Button(new Rect(1094, 619, 144, 33), "Interact [E]")) Interact();
            GUI.Label(new Rect(38, 660, 1190, 30), saves.Status, textStyle);
            GUI.enabled = true;
        }

        private void DrawMenu()
        {
            Panel(new Rect(0, 0, 1280, 720), new Color(0, 0, 0, 0.28f));
            Panel(new Rect(24, 140, 354, 450), new Color(0.07f, 0.13f, 0.15f, 0.99f));
            GUI.Label(new Rect(46, 158, 310, 35), "GAME MENU", titleStyle);
            GUI.enabled = pendingAction == null;
            if (Button(new Rect(46, 213, 146, 42), "Save")) saves.SaveLocal();
            if (Button(new Rect(208, 213, 146, 42), "Load")) ConfirmIfDirty("Load saved progress?", saves.LoadLocal);
            if (Button(new Rect(46, 271, 308, 42), "Export Saved Game")) saves.ExportJson();
            if (Button(new Rect(46, 329, 308, 42), "Import Saved Game")) ConfirmIfDirty("Import saved progress?", saves.ImportJson);
            if (Button(new Rect(46, 387, 308, 42), "Open Folder")) saves.OpenSaveFolder();
            if (Button(new Rect(46, 445, 308, 42), "New Session"))
            { pendingTitle = "Start a fresh session?"; pendingAction = saves.NewSession; }
            if (Button(new Rect(46, 513, 308, 42), "Return to Game [M]")) menuOpen = false;
            GUI.enabled = true;
        }

        private void DrawConfirmation()
        {
            Panel(new Rect(0, 0, 1280, 720), new Color(0, 0, 0, 0.55f));
            Panel(new Rect(430, 250, 620, 210), new Color(0.10f, 0.17f, 0.19f));
            GUI.Label(new Rect(456, 272, 565, 35), pendingTitle, headingStyle);
            GUI.Label(new Rect(456, 315, 565, 55), "This replaces the current session. Your local save file stays unchanged until you click Save.", textStyle);
            if (Button(new Rect(456, 397, 260, 38), "Continue"))
            { Action action = pendingAction; pendingAction = null; action(); }
            if (Button(new Rect(742, 397, 280, 38), "Cancel")) pendingAction = null;
        }

        private void DrawWorldLabels()
        {
            foreach (BuildingDefinition house in WorldLayout.Buildings)
            {
                string label = house.Name + (!house.CanRepair ? " [LOCKED]"
                    : saves.State.repairedBuildingIds.Contains(house.Id) ? " [REPAIRED]" : " [" + house.WoodCost + " wood]");
                WorldLabel(new Vector3(house.X, 3.7f, house.Z), label);
            }
            WorldLabel(new Vector3(WorldLayout.BridgeX, 1.8f, (WorldLayout.RiverSouth + WorldLayout.RiverNorth) / 2f), saves.State.bridgeRepaired ? "BRIDGE OPEN" : "BROKEN BRIDGE - 12 wood");
            WorldLabel(new Vector3(WorldLayout.MineX, 4.5f, WorldLayout.MineDoorZ + 3), "SEALED MINE");
        }

        private void WorldLabel(Vector3 worldPosition, string caption)
        {
            Vector3 point = sceneCamera.WorldToScreenPoint(worldPosition);
            float x = point.x * 1280f / Screen.width;
            float y = (Screen.height - point.y) * 720f / Screen.height;
            if (point.z < 0 || x < 120 || x > 1160 || y < 145 || y > 570) return;
            Rect rect = new Rect(x - 125, y - 15, 250, 30);
            Panel(rect, new Color(0.07f, 0.13f, 0.15f, 0.82f));
            GUI.Label(rect, caption, tagStyle);
        }

        private void DrawMiniMap()
        {
            Rect box = new Rect(1030, 20, 230, 208);
            Panel(box, new Color(0.07f, 0.13f, 0.15f, 0.96f));
            GUI.Label(new Rect(1042, 26, 210, 24), "TOWN MAP                 N", smallStyle);
            Rect map = new Rect(1044, 56, 202, 144);
            Panel(map, new Color(0.27f, 0.36f, 0.29f));
            Vector2 south = MiniPoint(map, WorldLayout.MinX, WorldLayout.RiverSouth);
            Vector2 north = MiniPoint(map, WorldLayout.MaxX, WorldLayout.RiverNorth);
            Panel(new Rect(map.x, north.y, map.width, south.y - north.y), new Color(0.24f, 0.48f, 0.58f));
            Vector2 bridge = MiniPoint(map, WorldLayout.BridgeX, (WorldLayout.RiverSouth + WorldLayout.RiverNorth) / 2f);
            Panel(new Rect(bridge.x - 4, north.y, 8, south.y - north.y), saves.State.bridgeRepaired ? Color.white : new Color(0.65f, 0.29f, 0.22f));
            foreach (BuildingDefinition house in WorldLayout.Buildings)
            {
                Vector2 p = MiniPoint(map, house.X, house.Z);
                Color color = !house.CanRepair ? Color.gray : saves.State.repairedBuildingIds.Contains(house.Id)
                    ? new Color(0.4f, 0.85f, 0.55f) : new Color(0.88f, 0.64f, 0.35f);
                Panel(new Rect(p.x - 4, p.y - 4, 8, 8), color);
            }
            for (int i = 0; i < WorldLayout.Resources.Length; i++)
            {
                if (saves.State.collectedWoodIds.Contains(i)) continue;
                ResourcePoint resource = WorldLayout.Resources[i];
                Vector2 p = MiniPoint(map, resource.X, resource.Z);
                Panel(new Rect(p.x - 1.5f, p.y - 1.5f, 3, 3), new Color(0.95f, 0.77f, 0.38f));
            }
            Vector2 mine = MiniPoint(map, WorldLayout.MineX, WorldLayout.MineDoorZ + 4);
            Panel(new Rect(mine.x - 5, mine.y - 4, 10, 8), Color.black);
            Vector2 playerPoint = MiniPoint(map, player.position.x, player.position.z);
            Panel(new Rect(playerPoint.x - 3, playerPoint.y - 3, 6, 6), Color.white);
            GUI.Label(new Rect(1042, 202, 210, 22), "White: you   Gold: wood", smallStyle);
        }

        private Vector2 MiniPoint(Rect map, float x, float z)
        {
            return new Vector2(map.x + (x - WorldLayout.MinX) / (WorldLayout.MaxX - WorldLayout.MinX) * map.width,
                map.yMax - (z - WorldLayout.MinZ) / (WorldLayout.MaxZ - WorldLayout.MinZ) * map.height);
        }

        private void ConfirmIfDirty(string title, Action action)
        {
            if (!saves.HasUnsavedChanges) { action(); return; }
            pendingTitle = title; pendingAction = action;
        }

        private void OnDestroy()
        {
            if (saves != null) { saves.StateChanged -= RefreshWorld; WorldLayout.ResetLayout(); }
            if (!Application.isPlaying) return;
            foreach (Material material in materials) if (material != null) Destroy(material);
            if (uiFont != null) Destroy(uiFont);
        }
    }
}
