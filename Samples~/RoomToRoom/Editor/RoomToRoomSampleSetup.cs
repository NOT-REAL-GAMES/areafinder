using System.IO;
using NotRealGames.Areafinder.Samples.RoomToRoom;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NotRealGames.Areafinder.Samples.RoomToRoom.Editor
{
    internal static class RoomToRoomSampleSetup
    {
        [MenuItem("Tools/Areafinder/Samples/Create Room-to-Room Demo")]
        public static void CreateDemo()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            string folder = AssetDatabase.GenerateUniqueAssetPath("Assets/Areafinder Room-to-Room Demo");
            AssetDatabase.CreateFolder("Assets", Path.GetFileName(folder));

            SemanticRegistryAsset registry = CreateAsset<SemanticRegistryAsset>(folder, "Semantic Registry.asset");
            SemanticId walkable = registry.Add("Walkable", "Traversable ground in this sample.");
            SemanticMask walkableMask = new SemanticMask(registry.SlotCapacity);
            walkableMask.Set(0);

            NavigationAreaAsset leftArea = CreateArea(folder, "Left Room.asset", -2d, walkableMask);
            NavigationAreaAsset rightArea = CreateArea(folder, "Right Room.asset", 2d, walkableMask);
            NavigationPolygonRecord leftPolygon = leftArea.Polygons[0];
            NavigationPolygonRecord rightPolygon = rightArea.Polygons[0];

            NavigationWorldAsset world = CreateAsset<NavigationWorldAsset>(folder, "Room-to-Room World.asset");
            world.SetSemanticRegistry(registry);
            world.AddArea(leftArea);
            world.AddArea(rightArea);
            NavigationPortalRecord portal = world.AddPortal(
                EdgeSpan(leftArea, leftPolygon, 2),
                EdgeSpan(rightArea, rightPolygon, 0),
                PortalDirection.Bidirectional,
                0.25d,
                PortalTransform.Identity);
            portal.SetSemantics(walkableMask);

            TraversalPolicyAsset policy = CreateAsset<TraversalPolicyAsset>(folder, "Walker Policy.asset");
            policy.SetRegistry(registry);
            var requiresWalkable = new SemanticPredicate(walkableMask, null, null);
            policy.SetEligibility(NavigationElementKind.Area, requiresWalkable);
            policy.SetEligibility(NavigationElementKind.Polygon, requiresWalkable);
            policy.SetEligibility(NavigationElementKind.Portal, requiresWalkable);
            policy.SetCostRule(walkable, 1d, 0d);
            world.AddPolicy(policy);

            NavigationBakeAsset bake = CreateAsset<NavigationBakeAsset>(
                folder,
                "Room-to-Room World Bake.asset");
            NavigationBakeResult bakeResult = NavigationBaker.Bake(world, bake);
            if (!bakeResult.Succeeded)
            {
                LogBakeFailure(world, bakeResult);
                Selection.activeObject = world;
                return;
            }

            EditorUtility.SetDirty(registry);
            EditorUtility.SetDirty(leftArea);
            EditorUtility.SetDirty(rightArea);
            EditorUtility.SetDirty(world);
            EditorUtility.SetDirty(policy);
            EditorUtility.SetDirty(bake);
            AssetDatabase.SaveAssets();

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            CreateFloor("Left Room", new Vector3(-2f, -0.05f, 0f));
            CreateFloor("Right Room", new Vector3(2f, -0.05f, 0f));
            CreateCameraAndLight();

            var previewObject = new GameObject("Areafinder Path Preview");
            RoomToRoomPathPreview preview = previewObject.AddComponent<RoomToRoomPathPreview>();
            preview.Configure(world, bake, policy, leftArea, rightArea);

            string scenePath = $"{folder}/RoomToRoom.unity";
            EditorSceneManager.SaveScene(scene, scenePath);
            Selection.activeGameObject = previewObject;
            SceneView.lastActiveSceneView?.FrameSelected();
            Debug.Log(
                $"Created the Areafinder room-to-room demo at '{folder}'. Enter Play Mode with Gizmos enabled.",
                previewObject);
        }

        private static NavigationAreaAsset CreateArea(
            string folder,
            string fileName,
            double universeX,
            SemanticMask walkableMask)
        {
            NavigationAreaAsset area = CreateAsset<NavigationAreaAsset>(folder, fileName);
            area.SetFrame(new AreaFrame(new Double3(universeX, 0d, 0d), Quaternion.identity));
            area.SetSemantics(walkableMask);
            NavigationPolygonRecord polygon = area.AddPolygon(new[]
            {
                new Vector3(-2f, 0f, -2f),
                new Vector3(-2f, 0f, 2f),
                new Vector3(2f, 0f, 2f),
                new Vector3(2f, 0f, -2f)
            });
            polygon.SetSemantics(walkableMask);
            return area;
        }

        private static PortalEntrySpan EdgeSpan(
            NavigationAreaAsset area,
            NavigationPolygonRecord polygon,
            int edgeIndex)
        {
            NavigationVertexRecord start = polygon.Vertices[edgeIndex];
            NavigationVertexRecord end = polygon.Vertices[(edgeIndex + 1) % polygon.Vertices.Count];
            return new PortalEntrySpan(
                area.Id,
                polygon.Id,
                start.OutgoingEdgeId,
                start.Position,
                end.Position);
        }

        private static T CreateAsset<T>(string folder, string fileName) where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            asset.name = Path.GetFileNameWithoutExtension(fileName);
            AssetDatabase.CreateAsset(asset, $"{folder}/{fileName}");
            return asset;
        }

        private static void CreateFloor(string name, Vector3 position)
        {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = name;
            floor.transform.position = position;
            floor.transform.localScale = new Vector3(4f, 0.1f, 4f);
            UnityEngine.Object.DestroyImmediate(floor.GetComponent<Collider>());
        }

        private static void CreateCameraAndLight()
        {
            var cameraObject = new GameObject("Main Camera", typeof(Camera));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetPositionAndRotation(
                new Vector3(0f, 10f, -10f),
                Quaternion.Euler(45f, 0f, 0f));

            var lightObject = new GameObject("Directional Light", typeof(Light));
            Light light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        private static void LogBakeFailure(NavigationWorldAsset world, NavigationBakeResult result)
        {
            for (int index = 0; index < result.Issues.Count; index++)
            {
                NavigationValidationIssue issue = result.Issues[index];
                if (issue.Severity == NavigationValidationSeverity.Error)
                {
                    Debug.LogError($"{issue.Code}: {issue.Message}", world);
                }
            }
        }
    }
}
