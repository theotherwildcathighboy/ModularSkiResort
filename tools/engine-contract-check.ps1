# engine-contract-check.ps1 - the engine internals the mod's meaning stands on, as (file, regex) pairs (R-5, DECISIONS 369).
# Its sibling tools/game-surface-check.js freezes the shape (types, field layouts, enum numbers, anchors, prefab fields, reading list).
# These rows pin the behavior a shape cannot show. Both run on patch day, docs/PIPELINE.md section 7.
#
# WHY. The lifts depend on about thirty private details of Game.dll: how the game draws a chair, how it spawns a sub-object,
# how the rope lanes anchor to stubs, which net flags the power-line class forces, how the electricity graph reads a prefab.
# None of it is an API. A CS2 patch can change any of it silently (1.6.0 -> 1.6.2 renumbered NetInitializeSystem and moved
# nothing that mattered). Learning this took a by-hand diff. This script turns "somebody remembers to diff" into a gate.
#
# HOW. Each row below is (id, decompiled file relative to the decompile root, regex, what breaks / where we cite it). The script
# matches the regex with Singleline over the whole file, so a row can pin a multi-line shape. Local variable names are \w+ because
# the decompiler renames them from build to build. A row that does not match is MISSING. Then do these steps:
#   1. Read the new file.
#   2. Decide whether the meaning moved.
#   3. Fix the regex (meaning unchanged) or the lift code and PIPELINE.md (meaning changed).
# Never loosen a regex just to turn the run green.
#
# ALSO. Section B greps the lift systems for entity creation without an Owner. Better Bulldozer marks every entity that has an
# Owner component whose m_Owner is Entity.Null as Deleted on load. So create a lift entity with a real owner or with no Owner at all
# (an allow-list names the three intentional ownerless creators). A `new Owner(Entity.Null)`, a `default(Owner)`
# or a RemoveComponent<Owner> in a lift system is a FAIL.
#
# USAGE.  powershell -NoProfile -ExecutionPolicy Bypass -File tools\engine-contract-check.ps1 [-Decompile <folder>] [-Quiet]
#   -Systems    the lift systems folder for section B (default mod\SkiResortMod\Systems, a test hook)
#   -Decompile  a decompile root that holds Game.Net\, Game.Objects\ ... (default: tools\decompile\<version> under ClaudeProjects, with the
#               version from the lock's version line, the one place it is written). The citation baseline 1.6.0f1 must also pass.
#               The file:line cites refer to it.
# EXIT. 0 = every signature found and the Owner audit clean. Otherwise the number of missing signatures + Owner findings
#       (2 and a message if the decompile folder or a source folder is not there). Read-only: writes nothing, deploys nothing.
# Maintenance: a new engine dependency = one more row here in the same round as the code that needs it (CLAUDE.md "lessons
# must become gates"). The PIPELINE.md Gates table ("engine contract") says so too.

param(
    [string]$Decompile = "",
    [string]$Systems = (Join-Path $PSScriptRoot "..\mod\SkiResortMod\Systems"),
    [switch]$Quiet
)

$ErrorActionPreference = "Stop"
if (-not $Decompile) {
    $lockVersion = (Select-String -Path (Join-Path $PSScriptRoot "game-surface.lock") -Pattern '^version (\S+)' | Select-Object -First 1).Matches[0].Groups[1].Value
    $Decompile = Join-Path $PSScriptRoot "..\..\tools\decompile\$lockVersion"
}
if (-not (Test-Path $Decompile)) { Write-Output "FATAL: decompile folder not found: $Decompile"; exit 2 }
$Decompile = (Resolve-Path $Decompile).Path

# ---- A. the signatures -----------------------------------------------------------------------------------------------------
# Cites are decompile 1.6.0f1 file:line (the baseline); 1.6.2f1 may have moved the line, the regex does not care.
$rows = @(
    # --- chair / cabin render path (a chair is a static object whose Transform we write) ---
    @('RND-1', 'Game.Rendering\PreCullingSystem.cs',
      'm_InterpolatedTransformData\.HasComponent\(reference\.m_Entity\)\)\s*\{\s*reference\.m_Flags \|= PreCullingFlags\.InterpolatedTransform;',
      'an InterpolatedTransform on the entity sets the culling flag that makes the renderer read it (PreCullingSystem.cs:1918-1920); without it chairs freeze or vanish'),
    @('RND-2', 'Game.Rendering\ObjectInterpolateSystem.cs',
      'PreCullingFlags\.InterpolatedTransform \| PreCullingFlags\.Animated \| PreCullingFlags\.Skeleton \| PreCullingFlags\.Emissive\)\) != 0 && \(\w+\.m_Flags & \(PreCullingFlags\.NearCamera \| PreCullingFlags\.Temp \| PreCullingFlags\.Relative\)\) == PreCullingFlags\.NearCamera',
      'the near-camera interpolation pass runs for InterpolatedTransform objects (ObjectInterpolateSystem.cs:252)'),
    @('RND-3', 'Game.Rendering\ObjectInterpolateSystem.cs',
      'valueRW = new InterpolatedTransform\(\w+\);',
      'the pass copies Transform into InterpolatedTransform each near-camera frame (ObjectInterpolateSystem.cs:296): that copy is how a written Transform reaches the screen'),
    @('RND-4', 'Game.Rendering\ObjectInterpolateSystem.cs',
      'case EmissiveProperties\.Purpose\.DecorativeLight:\s*\{\s*RequireElectricity\(ref electricity, owner\);',
      'station window lights (purpose 34) need the owner''s electricity (ObjectInterpolateSystem.cs:1089-1096; PIPELINE emissive row)'),
    @('RND-5', 'Game.Rendering\ObjectInterpolateSystem.cs',
      'case EmissiveProperties\.Purpose\.BuildingActive:\s*\{\s*if \(!isBuildingActive\)\s*\{\s*AnimateLight\(proceduralLight, ref emissive, ref light, deltaTime, 0f, instantReset\);',
      'purpose 72 is driven to 0 on a ServiceUpgrade child / inactive building (preflight EMISSIVE, DECISIONS 336; ObjectInterpolateSystem.cs:1194-1198)'),

    # --- sub-objects (towers, chairs, tower number tiles are all spawned this way) ---
    @('SUB-1', 'Game.Objects\SubObjectSystem.cs',
      'CreateEntity\(jobIndex, \w+\.m_Archetype\);\s*m_CommandBuffer\.AddComponent\(jobIndex, entity, new Owner\(owner\)\);\s*m_CommandBuffer\.SetComponent\(jobIndex, entity, new PrefabRef\(prefab\)\);\s*m_CommandBuffer\.SetComponent\(jobIndex, entity, \w+\);',
      'the sub-object recipe Owner + PrefabRef + Transform (SubObjectSystem.cs:2824-2827): TowerNumberTileSystem copies it for the plates'),
    @('SUB-2', 'Game.Objects\SubObjectSystem.cs',
      'EvenSpacing\) != 0\)\s*\{\s*Curve curve = m_NetCurveData\[\w+\];\s*float \w+ = MathUtils\.Length\(curve\.m_Bezier\.xz\);\s*int \w+ = \(int\)\(\w+ / math\.max\(1f, \w+\.m_Position\.z\) - 0\.5f\);',
      'EdgeMiddle EvenSpacing count n = (int)(L / s - 0.5) (SubObjectSystem.cs:1743): chair counts per span, LiftBatch counts assume it'),
    @('SUB-3', 'Game.Objects\SubObjectSystem.cs',
      'ownerElevation = math\.cmin\(m_NetElevationData\[\w+\.m_Owner\]\.m_Elevation\);',
      'a net node''s Elevation (min of both ends) picks the pillar-ladder member: LiftTowerFitSystem writes Elevation for this (SubObjectSystem.cs:980)'),
    @('SUB-4', 'Game.Objects\SubObjectSystem.cs',
      'case PillarType\.Standalone:\s*\{\s*if \(m_PrefabObjectGeometryData\.TryGetComponent\(\w+, out var \w+\)\)\s*\{\s*if \(m_PrefabPlaceableObjectData\.TryGetComponent\(\w+, out var \w+\)\)\s*\{\s*\w+\.m_Size\.y -= \w+\.m_PlacementOffset\.y;\s*\}\s*float \w+ = ownerElevation \+ \w+\.m_Position\.y - ownerTransform\.m_Position\.y;\s*float \w+ = \w+\.m_Size\.y - \w+;\s*\w+ \+= 1f / \(1f \+ math\.select\(2f \* \w+, 0f - \w+, \w+ < 0f\)\);',
      'the Standalone pillar scoring that chooses a tower height from the node Elevation (SubObjectSystem.cs:2189-2199); the planner assumes it'),
    @('SUB-5', 'Game.Objects\SubObjectSystem.cs',
      'case PillarType\.Standalone:\s*flags \|= SubObjectFlags\.AnchorTop;\s*flags \|= SubObjectFlags\.OnGround;\s*break;[\s\S]{0,400}?\.m_Bounds\.max\.y -= \w+\.m_PlacementOffset\.y;\s*\w+\.m_Position\.y -= \w+\.m_Bounds\.max\.y;',
      'a standalone tower is top-anchored: pivot = node y + spec y - member top (SubObjectSystem.cs:2464-2469; PIPELINE row 339)'),
    @('SUB-6', 'Game.Objects\SubObjectSystem.cs',
      'int \w+ = \w+ \* \w+\.Length \+ \w+;\s*CreateSubObject\(jobIndex, ref random, ref subRandom,',
      'an EvenSpacing sub-object''s align index = slot x row count + row (SubObjectSystem.cs:1751): ChairPinSystem pairs a carrier body with its grip by (edge, slot, side) from it (carrier grip round)'),
    @('SUB-7', 'Game.Objects\SubObjectSystem.cs',
      'm_CommandBuffer\.AddComponent\(jobIndex, entity, new Aligned\(\(ushort\)alignIndex\)\);',
      'the align index is stored as Aligned.m_SubObjectIndex on the new sub-object (SubObjectSystem.cs:2894): what the grip pairing reads'),
    # --- wind (the carrier swing reads the map on the main thread) ---
    @('WIND-1', 'Game.Simulation\WindSystem.cs',
      'public static readonly int kUpdateInterval = 512;[\s\S]*?public static Wind GetWind\(float3 position, NativeArray<Wind> windMap\)\s*\{\s*int2 cell = CellMapSystem<Wind>\.GetCell\(',
      'the wind map changes every 512 frames and GetWind is the bilinear map sample (WindSystem.cs:45, :69-80): ChairPinSystem / TramShuttleSystem poll it at that cadence for the carrier swing'),
    @('WIND-2', 'Game.Simulation\CellMapSystem.cs',
      'public NativeArray<T> GetMap\(bool readOnly, out JobHandle dependencies\)\s*\{\s*dependencies = \(readOnly \? m_WriteDependencies',
      'GetMap(readOnly) hands out the writers'' handle (CellMapSystem.cs:154-158): the swing completes it and reads synchronously, so it needs no AddReader'),
    # --- rope lanes: the anchor rewrite that closes the cable loop on a station stub, and sag ---
    @('LANE-1', 'Game.Net\LaneSystem.cs',
      'private bool FindAnchor\(ref float3 position, ref PathNode pathNode, Entity prefab, NativeList<LaneAnchor> anchors\)\s*\{[\s\S]{0,900}?if \(value\.m_Prefab == prefab\)[\s\S]{0,300}?position = value\.m_Position;\s*pathNode = value\.m_PathNode;',
      'FindAnchor matches by lane PREFAB and rewrites position + PathNode (LaneSystem.cs:4049-4072): the loop closes only if stub and line use the same lane prefab'),
    @('LANE-2', 'Game.Net\LaneSystem.cs',
      '\(laneFlags & LaneFlags\.FindAnchor\) != 0\)\s*\{\s*@?\w+\.x = canAnchor\.x && !FindAnchor\(ref curveData\.m_Bezier\.a, ref laneData\.m_StartNode, \w+\.m_Lane, startAnchors\);\s*@?\w+\.y = canAnchor\.y && !FindAnchor\(ref curveData\.m_Bezier\.d, ref laneData\.m_EndNode, \w+\.m_Lane, endAnchors\);',
      'the FindAnchor lane flag drives that rewrite at both ends (LaneSystem.cs:4117-4120)'),
    @('LANE-3', 'Game.Net\LaneSystem.cs',
      'UtilityLaneData \w+ = m_UtilityLaneData\[\w+\.m_Lane\];\s*if \(\w+\.m_Hanging != 0f\)[\s\S]{0,400}?math\.distance\(curveData\.m_Bezier\.a\.xz, curveData\.m_Bezier\.d\.xz\) \* \w+\.m_Hanging \* 1\.3333334f;',
      'rope sag = horizontal span * m_Hanging * 4/3 at the control points (LaneSystem.cs:4128-4134): ChairPin rebuilds the same parabola, PIPELINE rope-sag row'),
    @('LANE-4', 'Game.Prefabs\UtilityLaneData.cs',
      'public float m_VisualCapacity;\s*public float m_Hanging;\s*public UtilityTypes m_UtilityTypes;',
      'UtilityLaneData layout the loop cache reads (m_Hanging)'),

    # --- the power-line net class: what NetInitializeSystem forces on our line prefabs ---
    @('NET-1', 'Game.Prefabs\NetInitializeSystem.cs',
      '\w+\.m_EdgeLengthRange\.max = \w+\.m_MaxPylonDistance;\s*\w+\.m_ElevatedLength = \w+\.m_MaxPylonDistance;\s*\w+\.m_Hanging = \w+\.m_Hanging;\s*\w+\.m_Flags \|= GeometryFlags\.StrictNodes \| GeometryFlags\.LoweredIsTunnel \| GeometryFlags\.RaisedIsElevated;',
      'a PowerLinePrefab''s span cap = m_MaxPylonDistance, hanging copied, StrictNodes + Lowered/Raised semantics (NetInitializeSystem.cs:2170-2196)'),
    @('NET-2', 'Game.Prefabs\NetInitializeSystem.cs',
      'GeometryFlags\.StraightEdges \| GeometryFlags\.NoEdgeConnection \| GeometryFlags\.SnapToNetAreas \| GeometryFlags\.BlockZone \| GeometryFlags\.StandingNodes;',
      'straight capped spans, ground-standing nodes, zone block (same block): the reason the power-line class is the lift net'),
    @('NET-3', 'Game.Prefabs\NetInitializeSystem.cs',
      'if \(!flag2\)\s*\{\s*\w+\.m_Flags \|= GeometryFlags\.FlattenTerrain;\s*\}',
      'a non-marker power line flattens terrain (the trench under a lift); a MarkerNet is exempt (NetInitializeSystem.cs:2184-2195)'),
    @('NET-4', 'Game.Prefabs\NetInitializeSystem.cs',
      'case ElectricityConnection\.Voltage\.Low:\s*\w+ = Layer\.PowerlineLow;\s*\w+ = Layer\.Road \| Layer\.PowerlineLow;\s*\w+ = 4f;\s*break;\s*case ElectricityConnection\.Voltage\.High:\s*\w+ = Layer\.PowerlineHigh;\s*\w+ = Layer\.PowerlineHigh;\s*\w+ = 8f;',
      'Voltage High = layer PowerlineHigh only (what snaps lift lines to our HV stubs); Low also joins Road (NetInitializeSystem.cs:2416-2425; DECISIONS 369)'),
    @('NET-5', 'Game.Prefabs\NetInitializeSystem.cs',
      'netData\.m_NodePriority \+= compositionData\.m_Width;',
      'node prefab priority = composition width: a stub marker must be wider than every line (NetInitializeSystem.cs:135; preflight STUBNET)'),
    @('NET-6', 'Game.Tools\GenerateNodesSystem.cs',
      'if \(netData\.m_NodePriority > num\)[\s\S]{0,1500}?if \(netData4\.m_NodePriority > num\)',
      'the node takes the connected net with the strictly highest priority (GenerateNodesSystem.cs:1690-1765): a tie keeps the first edge'),
    @('NET-7', 'Game.Net\NetUtils.cs',
      'public static bool CanConnect\(NetData netData1, NetData netData2\)\s*\{\s*if \(\(netData1\.m_RequiredLayers & netData2\.m_ConnectLayers\) != netData1\.m_RequiredLayers\)\s*\{\s*return \(netData2\.m_RequiredLayers & netData1\.m_ConnectLayers\) == netData2\.m_RequiredLayers;',
      'which nets may meet: layers only (NetUtils.cs:499-506): why a city HV line can snap to a lift tower'),
    @('NET-8', 'Game.Net\NetUtils.cs',
      'public static Bezier4x3 StraightCurve\(float3 startPos, float3 endPos\)[\s\S]{0,900}?public static Bezier4x3 StraightCurve\(float3 startPos, float3 endPos, float hanging\)',
      'the straight-edge curve both our fit and the vanilla tool use for a span (NetUtils.cs:167-178)'),
    @('NET-9', 'Game.Simulation\GroundHeightSystem.cs',
      '\(\w+\.m_Flags & Game\.Net\.GeometryFlags\.FlattenTerrain\) != 0;',
      'the ground-height pass treats FlattenTerrain nets specially (GroundHeightSystem.cs:661-667, :747-749): the fit''s uncut sampler exists because of it'),
    @('NET-10', 'Game.Common\SystemOrder.cs',
      'updateSystem\.UpdateAt<ElectricityGraphSystem>\(SystemUpdatePhase\.Deserialize\);',
      'the flow graph is rebuilt from the PREFAB on every load (SystemOrder.cs:841): a prefab capacity fix reaches old saves'),

    # --- the Options button "Delete all ski resort content" (SkiDeleteAllSystem, research/delete-all-2026-10-29.md) ---
    @('DEL-1', 'Game.Common\SystemOrder.cs',
      'updateSystem\.UpdateAt<SubElementDeleteSystem>\(SystemUpdatePhase\.PostTool\);',
      'SubElementDeleteSystem cascades a Deleted owner at PostTool (SystemOrder.cs:724): SkiDeleteAllSystem is anchored before it'),
    @('DEL-2', 'Game.Objects\SubElementDeleteSystem.cs',
      'else if \(!m_UpdatedData\.HasComponent\(subNet\.m_SubNet\)\)\s*\{\s*m_CommandBuffer\.RemoveComponent<Owner>\(unfilteredChunkIndex, subNet\.m_SubNet\);',
      'a station stub node a foreign edge still uses loses its Owner and stays (SubElementDeleteSystem.cs:126-130); an Updated stub would keep it: the delete never tags owned nodes'),
    @('DEL-3', 'Game.Net\ReferencesSystem.cs',
      'CollectionUtils\.RemoveValue\(buffer, new ConnectedEdge\(edge\)\);\s*CollectionUtils\.RemoveValue\(buffer2, new ConnectedEdge\(edge\)\);',
      'a Deleted edge leaves both nodes'' ConnectedEdge buffers (ReferencesSystem.cs:143-156): kept nodes beside a deleted lift edge stay consistent'),

    # --- the net tool seam our lift tool drives (CreationDefinition + NetCourse through the vanilla apply path) ---
    @('TOOL-1', 'Game.Tools\NetToolSystem.cs',
      'CreationDefinition \w+ = new CreationDefinition\s*\{\s*m_Prefab = m_NetPrefab,\s*m_SubPrefab = m_LanePrefab,\s*m_RandomSeed = \w+\.NextInt\(\)\s*\};\s*\w+\.m_Flags \|= CreationFlags\.SubElevation;\s*NetCourse \w+ = default\(NetCourse\);\s*\w+\.m_Curve = NetUtils\.StraightCurve\(',
      'the shape of one planned span: CreationDefinition(prefab, subprefab, seed, SubElevation) + NetCourse with a straight curve (NetToolSystem.cs:3603-3660)'),
    @('TOOL-2', 'Game.Tools\NetToolSystem.cs',
      '\w+\.m_StartPosition\.m_Flags \|= CoursePosFlags\.IsFirst;\s*\w+\.m_EndPosition\.m_Flags \|= CoursePosFlags\.IsLast;',
      'course end flags IsFirst / IsLast (same block): the tool sets them per span'),
    @('TOOL-3', 'Game.Tools\CourseSplitSystem.cs',
      'float \w+ = netGeometryData\.m_EdgeLengthRange\.max;\s*if \(\(creationDefinition\.m_Flags & CreationFlags\.SubElevation\) != 0\)[\s\S]{0,500}?int \w+ = \(int\)math\.ceil\(\(\w+ - 0\.16f\) / \w+\);\s*if \(\w+ > 1\)',
      'the engine splits a course longer than the edge cap into ceil((L - 0.16) / cap) equal parts (CourseSplitSystem.cs:3485-3493): the R-6 MISMATCH is this split'),
    @('TOOL-4', 'Game.Tools\NetToolSystem.cs',
      'm_ToolRaycastSystem\.rayOffset = new float3\(0f, 0f - \w+\.m_DefaultSurfaceHeight\.max - elevation, 0f\);',
      'the cursor ray offset reads NetGeometryData.m_DefaultSurfaceHeight.max: CursorAimSystem patches that field (NetToolSystem.cs:5893)'),
    @('TOOL-5', 'Game.Tools\CreationDefinition.cs',
      'public Entity m_Prefab;[\s\S]{0,200}?public Entity m_SubPrefab;[\s\S]{0,400}?public int m_RandomSeed;',
      'CreationDefinition fields the lift tool fills'),

    # --- electricity: how a station stub, a lift edge and a consumer are wired into the flow graph ---
    @('ELE-1', 'Game.Simulation\ElectricityBuildingGraphSystem.cs',
      'm_ElectricityValveConnections\.HasComponent\(subNet\) \|\| IsOrphan\(subNet\)\) && !m_Deleted\.HasComponent\(subNet\)',
      'a building''s sub-net node joins its power only if it has a valve connection or is an orphan (ElectricityBuildingGraphSystem.cs:206): PowerBridgeSystem adds the valve'),
    @('ELE-2', 'Game.Simulation\ElectricityBuildingGraphSystem.cs',
      'private bool IsOrphan\(Entity netNode\)\s*\{\s*if \(m_NetOrphans\.HasComponent\(netNode\)\)\s*\{\s*return true;\s*\}[\s\S]{0,300}?if \(m_Owners\.HasComponent\(item\.m_Edge\)\)\s*\{\s*return false;',
      'an OWNED edge makes the stub non-orphan (:220-237): why an owned stub needs the valve'),
    @('ELE-3', 'Game.Simulation\ElectricityBuildingGraphSystem.cs',
      'CreateOrUpdateFlowEdge\(jobIndex, bufferedEntity2, buildingNodes\.m_ConsumerNode, FlowDirection\.Forward, markerNodeData\.m_Capacity\);',
      'a consumer is fed from the valve node with the MARKER net''s capacity (:385): the stub prefab, not the lift line, caps a station'),
    @('ELE-4', 'Game.Serialization\ElectricityGraphSystem.cs',
      'componentData\.m_Capacity = 400000;\s*componentData\.m_Direction = FlowDirection\.Both;',
      'a net prefab with no ElectricityConnectionData loads as 400000 / Both (Serialization/ElectricityGraphSystem.cs:68-77)'),
    @('ELE-5', 'Game.Simulation\ElectricityEdgeGraphSystem.cs',
      'ComponentType\.ReadOnly<Game\.Net\.ElectricityConnection>\(\), ComponentType\.ReadOnly<Edge>\(\), ComponentType\.ReadOnly<PrefabRef>\(\), ComponentType\.ReadOnly<Created>\(\), ComponentType\.Exclude<Temp>\(\)',
      'a flow edge is created per freshly Created net edge that carries ElectricityConnection (ElectricityEdgeGraphSystem.cs:208): every lift section adds one'),
    @('ELE-6', 'Game.Simulation.Flow\Edge.cs',
      'public int GetCapacity\(bool backwards\)\s*\{\s*if \(backwards\)\s*\{\s*if \(\(m_Direction & FlowDirection\.Backward\) == FlowDirection\.None\)|public int GetCapacity\(bool backwards\)\s*\{\s*if \(backwards\)\s*\{\s*if \(\(m_Direction & FlowDirection\.Backward\) == 0\)',
      'the edge capacity is gated by Direction and is the max-flow limit (Simulation.Flow/Edge.cs:28-43): R-1 capacity reasoning'),
    @('ELE-7', 'Game.Prefabs\ConsumptionData.cs',
      'if \(m_ElectricityConsumption > 0f\)\s*\{\s*components\.Add\(ComponentType\.ReadWrite<ElectricityConsumer>\(\)\);',
      'ServiceConsumption 0 builds no ElectricityConsumer: the top station reads no power (ConsumptionData.cs:21-26; LiftCensusSystem header)'),
    @('ELE-8', 'Game.Buildings\ElectricityConsumerFlags.cs',
      'None = 0,\s*Connected = 1,\s*NoElectricityWarning = 2,\s*BottleneckWarning = 4',
      'the flag bits LiftServedRule.FlagConnected / FlagNoElectricityWarning / FlagBottleneckWarning mirror (ElectricityConsumerFlags.cs:5-12)'),
    @('ELE-9', 'Game.Simulation\DispatchElectricitySystem.cs',
      'consumer\.m_Flags \|= \(ElectricityConsumerFlags\)\(beyondBottleneck \? 4 : 2\);',
      'when fulfilled < wanted for kAlertCooldown ticks the warning flag is set (DispatchElectricitySystem.cs:126-158): the lifts'' power read'),
    @('ELE-10', 'Game.Buildings\ElectricityConsumer.cs',
      'public int m_WantedConsumption;\s*public int m_FulfilledConsumption;\s*public short m_CooldownCounter;\s*public ElectricityConsumerFlags m_Flags;',
      'the consumer layout ReadLiftPower reads'),
    @('ELE-11', 'Game.Prefabs\ElectricityConnection.cs',
      'public Voltage m_Voltage;\s*public FlowDirection m_Direction = FlowDirection\.Both;\s*public int m_Capacity;',
      'the prefab fields the POWERFLOW preflight rule pins (Low = 0, High = 1)'),

    # --- snow: SkiSnowInjectionSystem writes into the vanilla snow textures, and they go into the save ---
    @('SNOW-1', 'Game.Simulation\SnowSystem.cs', 'private const int kTexSize = 1024;',
      'the snow texture is 1024 square: the injection''s texel math; its [buffer] guard (SkiSnowInjectionSystem) turns a change into a skipped write, not a bad one'),
    @('SNOW-2', 'Game.Simulation\SnowSystem.cs', 'private const float kSnowHeightScale = 8f;',
      'the meters a snow unit means: run depth, the decal dress and the props'' snow all read through it'),
    @('SNOW-3', 'Game.Simulation\SnowSystem.cs', 'public RenderTexture SnowDepth',
      'how the injection finds the current side of the flip-flop texture'),
    @('SNOW-4', 'Game.Simulation\SnowSystem.cs', 'AsyncGPUReadback\.RequestIntoNativeArray\(ref \w+, m_SnowHeights\[Read\]\);',
      'the save reads the Read side: an injection on the wrong side vanishes on the next load'),
    @('SNOW-5', 'Game.Simulation\SnowSystem.cs', 'm_LoadKernelR16G16_UNorm',
      'the snow texture format the injection writes (R16G16_UNorm; the same guard)'),

    # --- terrain and leisure: cat track grading and the ski day ---
    @('TERR-1', 'Game.Simulation\TerrainSystem.cs', 'm_AsyncGPUReadback\.Request\(m_HeightmapCascade',
      'the heightmap readback that makes graded terrain visible to CPU height queries'),
    @('TERR-2', 'Game.Simulation\TerrainSystem.cs', 'public TerrainHeightData GetHeightData\(bool waitForPending = false\)',
      'what FoldRowsJob and the slope rater hold'),
    @('LEIS-1', 'Game.Simulation\CitizenBehaviorSystem.cs', '\(uint\)\(\w+ \* 262144f\);\s*Leisure \w+ = new Leisure\s*\{\s*m_LastPossibleFrame = m_SimulationFrame \+ \w+',
      'a citizen''s leisure window in frames (262144 a day): the ski day claims a citizen through this component'),
    @('LEIS-2', 'Game.Simulation\LeisureSystem.cs', 'return 262144 / kUpdatePerDay;',
      'the frames-per-day number copied into SkiDaySystem''s timing'),
    @('ATT-1', 'Game.Simulation\AttractionSystem.cs', 'public override int GetUpdateInterval\(SystemUpdatePhase phase\)\s*\{\s*return 16;',
      'the 16-frame cycle SkiResortAttractivenessSystem must land after to win'),
    @('ATT-2', 'Game.Simulation\AttractionSystem.cs', 'AttractivenessProvider \w+ = new AttractivenessProvider',
      'the vanilla write our value overrides'),

    # --- overlay and tools: the drawing jobs and the abstract members our subclasses implement (one more abstract member is a TypeLoadException) ---
    @('OVR-1', 'Game.Rendering\OverlayRenderSystem.cs', 'public Buffer GetBuffer\(out JobHandle dependencies\)',
      'the Burst drawing jobs hold this Buffer by value (GAMESURFACE row OverlayRenderSystem.Buffer)'),
    @('OVR-2', 'Game.Rendering\OverlayRenderSystem.cs', 'public void AddBufferWriter\(JobHandle handle\)',
      'how a drawing job hands its handle back: without it the buffer is read while the job writes'),
    @('OVR-3', 'Game.Rendering\OverlayRenderSystem.cs', 'public void DrawCircle\(Color outlineColor, Color fillColor, float outlineWidth, StyleFlags styleFlags, float2 direction, float3 position, float diameter\)',
      'the last argument is a diameter: it is compiled into our Burst library'),
    @('TOOL-1', 'Game.Tools\ToolBaseSystem.cs', 'public abstract string toolID', 'implemented by the four tool systems'),
    @('TOOL-2', 'Game.Tools\ToolBaseSystem.cs', 'public abstract PrefabBase GetPrefab\(\);', 'implemented by the four tool systems'),
    @('TOOL-3', 'Game.Tools\ToolBaseSystem.cs', 'public abstract bool TrySetPrefab\(PrefabBase prefab\);', 'implemented by the four tool systems'),
    @('TT-1', 'Game.UI.Tooltip\TooltipSystemBase.cs', 'protected void AddMouseTooltip\(IWidget \w+\)', 'used by the eight tooltip systems'),

    # --- update order: the ANCHORS rule of tools/game-surface-check.js stands on this ---
    @('ORD-1', 'Game\UpdateSystem.cs', 'if \(\w+\.m_Phase == \w+\.m_Phase\)',
      'an anchor walks only rows of its own phase: why a system anchored across phases never runs, with no log line (GAMESURFACE ANCHORS)'),

    # --- object bounds churn behind the 2 m chair refresh, and the save identity ---
    @('OBJ-1', 'Game.Objects\UpdateCollectSystem.cs',
      'ComponentType\.ReadOnly<Object>\(\),\s*ComponentType\.ReadOnly<Static>\(\),\s*ComponentType\.ReadOnly<Transform>\(\),\s*ComponentType\.ReadOnly<PrefabRef>\(\)\s*\},\s*Any = new ComponentType\[3\]\s*\{\s*ComponentType\.ReadOnly<Created>\(\),\s*ComponentType\.ReadOnly<Updated>\(\),\s*ComponentType\.ReadOnly<Deleted>\(\)',
      'every Updated Static object''s bounds are collected (UpdateCollectSystem.cs:195-215): the downstream cost of the chair bounds refresh (R-3)'),
    @('OBJ-2', 'Game.Prefabs\PrefabID.cs',
      'if \(string\.Equals\(m_Type, other\.m_Type\) && string\.Equals\(m_Name, other\.m_Name\)\)\s*\{\s*return m_Hash\.Equals\(other\.m_Hash\);',
      'a saved prefab is named by (type, name, hash): never rename / retype a shipped prefab (preflight SHIPPED; PrefabID.cs:45-52)'),
    @('OBJ-3', 'Game.Net\ConnectionWarningSystem.cs',
      'UpdateConnectionWarning\(owner, subNet, m_ElectricityParameterData\.m_LowVoltageNotConnectedPrefab, \(layer2 & Layer\.PowerlineLow\) != 0\);\s*UpdateConnectionWarning\(owner, subNet, m_ElectricityParameterData\.m_HighVoltageNotConnectedPrefab, \(layer2 & Layer\.PowerlineHigh\) != 0\);',
      'the not-connected icons a loose stub raises (ConnectionWarningSystem.cs:2363-2367): StationLvIconSystem removes the LV one')
)

$cache = @{}
function Get-Text([string]$rel) {
    if ($cache.ContainsKey($rel)) { return $cache[$rel] }
    $p = Join-Path $Decompile $rel
    $t = $null
    if (Test-Path $p) { $t = [IO.File]::ReadAllText($p) }
    $cache[$rel] = $t
    return $t
}

$missing = New-Object System.Collections.Generic.List[string]
$ok = 0
foreach ($r in $rows) {
    $id = $r[0]; $file = $r[1]; $pat = $r[2]; $why = $r[3]
    $text = Get-Text $file
    if ($null -eq $text) { $missing.Add("MISSING  $id  $file :: FILE NOT FOUND ($why)"); continue }
    if ([regex]::IsMatch($text, $pat, [Text.RegularExpressions.RegexOptions]::Singleline)) {
        $ok++
        if (-not $Quiet) { Write-Output "OK       $id  $file" }
    } else {
        $missing.Add("MISSING  $id  $file :: $why")
    }
}

# ---- B. lift systems: entity creation without an Owner (Better Bulldozer's load cleanup) -------------------------------------
$sysDir = $Systems
$ownerFindings = New-Object System.Collections.Generic.List[string]
$ownerSeen = 0; $ownerAllowed = 0
if (-not (Test-Path $sysDir)) {
    Write-Output "FATAL: lift systems folder not found: $sysDir"; exit 2
}
$liftFiles = Get-ChildItem $sysDir -Filter *.cs -File | Where-Object { $_.Name -match '^(Lift|Chair|Tram|TowerNumber|PowerBridge|Station)' }
# Intentionally ownerless creators (no Owner component is created, so Better Bulldozer's null-owner sweep never sees them):
#   LiftCensusSystem   m_EventArchetype      a vanilla event entity (Created/Updated/Deleted tag), consumed the same tick
#   LiftLineToolSystem ecb.CreateEntity()    a tool definition entity (CreationDefinition + NetCourse), consumed by the apply path
#   PowerBridgeSystem  nodeArchetype         electricity flow nodes (ElectricityFlowSystem.nodeArchetype), graph data, not world objects
$allow = @(
    @('LiftCensusSystem.cs',   'm_EventArchetype'),
    @('LiftLineToolSystem.cs', 'ecb\.CreateEntity\(\)'),
    @('LiftLineToolSystem.Sections.cs', 'ecb\.CreateEntity\(\)'),   # the section delete definition (CreationFlags.Delete)
    @('PowerBridgeSystem.cs',  'nodeArchetype')
)
foreach ($f in $liftFiles) {
    $lines = [IO.File]::ReadAllLines($f.FullName)
    for ($i = 0; $i -lt $lines.Length; $i++) {
        $ln = $lines[$i]
        $code = $ln -replace '//.*$', ''
        if ($code -match 'new Owner\(\s*Entity\.Null\s*\)|default\(Owner\)|RemoveComponent<Owner>') {
            $ownerFindings.Add("$($f.Name):$($i + 1): drops or nulls an Owner: $($ln.Trim())"); continue
        }
        if ($code -notmatch '\.(CreateEntity|Instantiate)\(') { continue }
        $ownerSeen++
        $window = ($lines[$i..([Math]::Min($i + 12, $lines.Length - 1))] -join "`n")
        if ($window -match 'new Owner\(') { continue }
        $hit = $false
        foreach ($a in $allow) { if ($f.Name -eq $a[0] -and $window -match $a[1]) { $hit = $true; break } }
        if ($hit) { $ownerAllowed++ } else { $ownerFindings.Add("$($f.Name):$($i + 1): entity created with no Owner within 12 lines and not allow-listed: $($ln.Trim())") }
    }
}

# ---- report ----------------------------------------------------------------------------------------------------------------
$total = $rows.Count
Write-Output ""
Write-Output "engine-contract-check: decompile $Decompile"
Write-Output "  A. signatures: $ok of $total present, $($missing.Count) missing"
foreach ($m in $missing) { Write-Output "  $m" }
Write-Output "  B. Owner audit: $($liftFiles.Count) lift system file(s), $ownerSeen creation site(s) ($ownerAllowed allow-listed ownerless), $($ownerFindings.Count) finding(s)"
foreach ($o in $ownerFindings) { Write-Output "  OWNER    $o" }
$bad = $missing.Count + $ownerFindings.Count
if ($bad -eq 0) { Write-Output "engine-contract-check: PASS" } else { Write-Output "engine-contract-check: FAIL ($bad)" }
exit $bad
