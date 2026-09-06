# Room to Room

This sample creates two manually authored polygon Areas joined by one bidirectional Portal. At runtime it submits an asynchronous path request and draws the returned steering guidance without moving an agent or requiring Unity NavMesh.

## Create the demo

1. Import **Room to Room** from Areafinder's Package Manager page.
2. Choose **Tools > Areafinder > Samples > Create Room-to-Room Demo**.
3. The command creates a unique `Assets/Areafinder Room-to-Room Demo` folder and opens its generated scene.
4. Enter Play Mode. Keep **Gizmos** enabled in the Scene or Game view.

Cyan outlines are authored polygon boundaries, magenta marks the Portal transition, and yellow shows the optional steering guidance returned by the path request. Select **Areafinder Path Preview** to inspect its request status or change the Area-local start and goal positions. Its **Request Path** context menu reruns the query in Play Mode.

## Edit and rebake

Select `Room-to-Room World.asset`, then choose **Assets > Areafinder > Open Authoring**. Move vertices, inspect the Portal, or change semantics with the Scene tool. After an authoring change, select the World asset and choose **Assets > Areafinder > Bake Selected World** before entering Play Mode again.

The source assets remain separate from the compiled bake: Areas and the Portal define authored topology, while `RoomToRoomPathPreview` consumes only the compiled policy and asynchronous path API. The component deliberately contains no locomotion behavior and does not require exact arrival at intermediate steering targets.
