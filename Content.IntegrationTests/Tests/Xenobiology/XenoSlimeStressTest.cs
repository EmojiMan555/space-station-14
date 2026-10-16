#nullable enable
using System.Diagnostics;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Xenobiology.Slime;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests.Xenobiology;

[TestFixture]
public sealed class XenoSlimeStressTest : GameTest
{
    // Compare: empty map, our slimes, vanilla HTN slimes. Run with:
    // dotnet test Content.IntegrationTests --filter "FullyQualifiedName~XenoSlimeStressTest"
    [TestCase(null, 0, "")]
    [TestCase("MobXenoSlimeGrey", 100, "")]
    [TestCase("MobAdultSlimesBlue", 100, "")]
    [Explicit("Slow benchmark, run by hand")]
    public async Task Stress(string? proto, int count, string mode = "")
    {
        var map = await Pair.LoadTestMap(new ResPath("/Maps/Test/npc_test_map.yml"));
        var mapSys = Server.System<SharedMapSystem>();
        await Server.WaitAssertion(() =>
        {
            if (proto == null)
                return;
            var grid = map.Grid;
            var tiles = mapSys.GetAllTiles(grid.Owner, grid.Comp).ToList();
            TestContext.Out.WriteLine($"floor tiles: {tiles.Count}");
            for (var i = 0; i < count; i++)
            {
                var t = tiles[(i * 7) % tiles.Count];
                var coords = mapSys.GridTileToLocal(grid.Owner, grid.Comp, t.GridIndices);
                var uid = Server.EntMan.SpawnAtPosition(proto, coords);
                if (Server.EntMan.TryGetComponent<XenoSlimeComponent>(uid, out var s))
                    s.MaxMergeCount = 0; // keep the count fixed for a fair test
                if (mode == "nobrain" || mode == "noslime")
                    Server.EntMan.RemoveComponent<Content.Server.Xenobiology.XenoSlimeBrainComponent>(uid);
                if (mode == "noslime")
                    Server.EntMan.RemoveComponent<XenoSlimeComponent>(uid);
                if (mode == "nohop" && Server.EntMan.TryGetComponent<Content.Server.Xenobiology.XenoSlimeBrainComponent>(uid, out var br))
                {
                    br.HopMinDistance = 0;
                    br.HopMaxDistance = 0;
                }
                if (mode == "nohtn")
                    Server.EntMan.RemoveComponent<Content.Server.NPC.HTN.HTNComponent>(uid);
            }
        });
        await RunSeconds(2); // warm up
        var sw = Stopwatch.StartNew();
        await RunSeconds(60);
        sw.Stop();
        TestContext.Out.WriteLine($"RESULT {proto ?? "none"} x{count} {mode}: 60 game-seconds took {sw.ElapsedMilliseconds} ms real time");
    }
}
