#nullable enable
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.Xenobiology;
using Content.Server.Gravity;
using Content.Shared.Gravity;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Xenobiology.Slime;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests.Xenobiology;

[TestFixture]
public sealed class XenoSlimeTest : GameTest
{
    private async Task<Robust.UnitTesting.Pool.TestMapData> Map()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitPost(() =>
        {
            // The default test map is ONE floor tile. Lay a 21x21 floor so things can walk.
            var mapSys = Server.System<SharedMapSystem>();
            for (var x = -10; x <= 10; x++)
            for (var y = -10; y <= 10; y++)
                mapSys.SetTile(map.Grid.Owner, map.Grid.Comp, new Robust.Shared.Maths.Vector2i(x, y), map.Tile.Tile);
            var g = Server.EntMan.EnsureComponent<GravityComponent>(map.Grid);
            Server.System<GravitySystem>().EnableGravity(map.Grid, g);
        });
        await RunSeconds(3); // let the pathfinding graph build for the new floor
        return map;
    }

    private int CountSlimes()
    {
        var n = 0;
        var q = Server.EntMan.EntityQueryEnumerator<XenoSlimeComponent>();
        while (q.MoveNext(out _, out _))
            n++;
        return n;
    }

    [Test]
    public async Task SplitTest()
    {
        var map = await Map();
        EntityUid parent = default;
        await Server.WaitAssertion(() => parent = Server.EntMan.SpawnAtPosition("MobXenoSlimeTestSplit", map.GridCoords));
        await RunSeconds(1);
        Assert.That(Server.EntMan.HasComponent<XenoSlimeSplittingComponent>(parent), "should be shaking");
        await RunSeconds(3);
        Assert.That(Server.EntMan.Deleted(parent), "parent should be gone");
        var count = CountSlimes();
        Assert.That(count, Is.InRange(2, 3), "2 or 3 babies");
        await Server.WaitAssertion(() =>
        {
            var q = Server.EntMan.EntityQueryEnumerator<XenoSlimeComponent>();
            while (q.MoveNext(out var uid, out var s))
            {
                Assert.That(s.MutationChance, Is.EqualTo(0.30f).Within(0.001f));
                Assert.That(s.Hunger, Is.LessThanOrEqualTo(90));
                TestContext.Out.WriteLine($"baby {Server.EntMan.ToPrettyString(uid)} mood {s.Mood}");
            }
        });
    }

    [Test]
    public async Task RainbowTest()
    {
        var map = await Map();
        await Server.WaitAssertion(() => Server.EntMan.SpawnAtPosition("MobXenoSlimeTestRainbow", map.GridCoords));
        await RunSeconds(4.5f);
        await Server.WaitAssertion(() =>
        {
            var q = Server.EntMan.EntityQueryEnumerator<XenoSlimeComponent, MetaDataComponent>();
            var n = 0;
            while (q.MoveNext(out _, out _, out var meta))
            {
                n++;
                Assert.That(meta.EntityPrototype?.ID, Is.EqualTo("MobXenoSlimeRainbow"));
            }
            Assert.That(n, Is.InRange(2, 3));
        });
    }

    [Test]
    public async Task MergeTest()
    {
        var map = await Map();
        EntityUid a = default, b = default;
        await Server.WaitAssertion(() =>
        {
            a = Server.EntMan.SpawnAtPosition("MobXenoSlimeTestMerge", map.GridCoords);
            b = Server.EntMan.SpawnAtPosition("MobXenoSlimeTestMerge", map.GridCoords.Offset(new System.Numerics.Vector2(0.2f, 0)));
            // stop them wandering apart
            Server.EntMan.RemoveComponent<XenoSlimeBrainComponent>(a);
            Server.EntMan.RemoveComponent<XenoSlimeBrainComponent>(b);
        });
        await RunSeconds(5);
        Assert.That(CountSlimes(), Is.EqualTo(1));
        var survivor = Server.EntMan.Deleted(a) ? b : a;
        var comp = Server.EntMan.GetComponent<XenoSlimeComponent>(survivor);
        Assert.That(comp.MergeCount, Is.EqualTo(1));
        TestContext.Out.WriteLine($"merged hunger {comp.Hunger}");
    }

    [Test]
    public async Task DifferentColorsDontMerge()
    {
        var map = await Map();
        await Server.WaitAssertion(() =>
        {
            var a = Server.EntMan.SpawnAtPosition("MobXenoSlimeTestMerge", map.GridCoords);
            var b = Server.EntMan.SpawnAtPosition("MobXenoSlimeOrange", map.GridCoords);
            Server.EntMan.RemoveComponent<XenoSlimeBrainComponent>(a);
            Server.EntMan.RemoveComponent<XenoSlimeBrainComponent>(b);
        });
        await RunSeconds(5);
        Assert.That(CountSlimes(), Is.EqualTo(2));
    }

    [Test]
    public async Task WanderTest()
    {
        var map = await Map();
        EntityUid s = default;
        await Server.WaitAssertion(() => s = Server.EntMan.SpawnAtPosition("MobXenoSlimeTestMerge", map.GridCoords));
        var xformSys = Server.System<SharedTransformSystem>();
        var start = xformSys.GetWorldPosition(s);
        var moved = false;
        var sawHop = false;
        for (var i = 0; i < 30; i++)
        {
            await RunSeconds(0.5f);
            var brain = Server.EntMan.GetComponent<XenoSlimeBrainComponent>(s);
            sawHop |= brain.State == XenoSlimeBrainState.Hopping;
            if ((xformSys.GetWorldPosition(s) - start).Length() > 0.5f)
                moved = true;
        }
        TestContext.Out.WriteLine($"end pos delta {(xformSys.GetWorldPosition(s) - start).Length()}");
        Assert.That(sawHop, "should have hopped");
        Assert.That(moved, "should have moved");
    }

    [Test]
    public async Task CorpseTest()
    {
        var map = await Map();
        EntityUid slime = default, corpse = default;
        await Server.WaitAssertion(() =>
        {
            corpse = Server.EntMan.SpawnAtPosition("MobHuman", map.GridCoords.Offset(new System.Numerics.Vector2(1.5f, 0)));
            Server.System<MobStateSystem>().ChangeMobState(corpse, MobState.Dead);
            slime = Server.EntMan.SpawnAtPosition("MobXenoSlimeGrey", map.GridCoords);
            Server.EntMan.GetComponent<XenoSlimeComponent>(slime).Hunger = 10;
        });
        var startHunger = Server.EntMan.GetComponent<XenoSlimeComponent>(slime).Hunger;
        var sawEating = false;
        for (var i = 0; i < 60; i++)
        {
            await RunSeconds(0.5f);
            if (Server.EntMan.Deleted(slime) || Server.EntMan.Deleted(corpse))
                break;
            var brain = Server.EntMan.GetComponent<XenoSlimeBrainComponent>(slime);
            sawEating |= brain.State == XenoSlimeBrainState.EatingCorpse;
        }
        Assert.That(sawEating, "should have eaten");
        var hunger = Server.EntMan.GetComponent<XenoSlimeComponent>(slime).Hunger;
        TestContext.Out.WriteLine($"hunger {startHunger} -> {hunger}, corpse deleted {Server.EntMan.Deleted(corpse)}");
        Assert.That(Server.EntMan.Deleted(corpse), "20 bites should finish the corpse");
        var brains = 0;
        var bq = Server.EntMan.EntityQueryEnumerator<Content.Shared.Body.Components.BrainComponent, TransformComponent>();
        while (bq.MoveNext(out var b, out _, out var bx))
        {
            brains++;
            TestContext.Out.WriteLine($"brain {Server.EntMan.ToPrettyString(b)} parent {Server.EntMan.ToPrettyString(bx.ParentUid)}");
        }
        TestContext.Out.WriteLine($"brains in world: {brains}");
    }

    [Test]
    public async Task HandFeedTest()
    {
        var map = await Map();
        EntityUid slime = default, human = default, food = default;
        await Server.WaitAssertion(() =>
        {
            slime = Server.EntMan.SpawnAtPosition("MobXenoSlimeTestMerge", map.GridCoords);
            Server.EntMan.RemoveComponent<XenoSlimeBrainComponent>(slime);
            human = Server.EntMan.SpawnAtPosition("MobHuman", map.GridCoords);
            food = Server.EntMan.SpawnAtPosition("FoodBanana", map.GridCoords);
        });
        await RunSeconds(0.5f);
        var before = Server.EntMan.GetComponent<XenoSlimeComponent>(slime).Hunger;
        bool started = false;
        await Server.WaitAssertion(() =>
        {
            started = Server.System<Content.Shared.Nutrition.EntitySystems.IngestionSystem>().TryIngest(human, slime, food);
        });
        await RunSeconds(10);
        var after = Server.EntMan.GetComponent<XenoSlimeComponent>(slime).Hunger;
        TestContext.Out.WriteLine($"feed started {started}, hunger {before} -> {after}");
        Assert.That(after, Is.GreaterThan(before));
    }

    [Test]
    public async Task FriendTest()
    {
        var map = await Map();
        EntityUid slime = default, human = default, rabid = default;
        await Server.WaitAssertion(() =>
        {
            slime = Server.EntMan.SpawnAtPosition("MobXenoSlimeTestMerge", map.GridCoords);
            rabid = Server.EntMan.SpawnAtPosition("MobXenoSlimeGrey", map.GridCoords);
            human = Server.EntMan.SpawnAtPosition("MobHuman", map.GridCoords);
        });
        await RunSeconds(0.5f);
        await Server.WaitAssertion(() =>
        {
            Server.System<XenoSlimeHungerSystem>().SetHunger((rabid, Server.EntMan.GetComponent<XenoSlimeComponent>(rabid)), 5);
            var tries = 0;
            while (!Server.EntMan.GetComponent<XenoSlimeComponent>(slime).Friends.Contains(human) && tries < 100)
            {
                var ev = new Content.Shared.Interaction.Events.InteractionSuccessEvent(human);
                Server.EntMan.EventBus.RaiseLocalEvent(slime, ref ev);
                tries++;
            }
            TestContext.Out.WriteLine($"calm became friend after {tries} pets");
            Assert.That(tries, Is.LessThan(100));
            for (var i = 0; i < 100; i++)
            {
                var ev = new Content.Shared.Interaction.Events.InteractionSuccessEvent(human);
                Server.EntMan.EventBus.RaiseLocalEvent(rabid, ref ev);
            }
            Assert.That(Server.EntMan.GetComponent<XenoSlimeComponent>(rabid).Mood, Is.EqualTo(SlimeMood.Rabid));
            Assert.That(Server.EntMan.GetComponent<XenoSlimeComponent>(rabid).Friends, Is.Empty);
        });
    }

    // ---------------- Steps 4 + 5 ----------------

    private EntityUid SpawnSlime(Robust.UnitTesting.Pool.TestMapData map, string proto, float x, double? hunger = null)
    {
        var uid = Server.EntMan.SpawnAtPosition(proto, map.GridCoords.Offset(new System.Numerics.Vector2(x, 0)));
        var s = Server.EntMan.GetComponent<XenoSlimeComponent>(uid);
        s.MaxMergeCount = 0;
        s.HungerDrain = 0;
        if (hunger != null)
            Server.System<XenoSlimeHungerSystem>().SetHunger((uid, s), hunger.Value);
        return uid;
    }

    [Test]
    public async Task ChaseAndLatchTest()
    {
        var map = await Map();
        EntityUid slime = default, mouse = default;
        await Server.WaitAssertion(() =>
        {
            slime = SpawnSlime(map, "MobXenoSlimeGrey", 0, 100);   // Calm
            mouse = Server.EntMan.SpawnAtPosition("MobMouse", map.GridCoords.Offset(new System.Numerics.Vector2(2.5f, 0)));
            Server.EntMan.RemoveComponent<Content.Server.NPC.HTN.HTNComponent>(mouse); // keep it still
        });
        var latched = false;
        var states = new System.Collections.Generic.List<string>();
        for (var i = 0; i < 200 && !latched; i++)
        {
            await RunTicksSync(1);
            var br = Server.EntMan.GetComponent<XenoSlimeBrainComponent>(slime);
            Server.EntMan.TryGetComponent<Content.Server.NPC.Components.NPCSteeringComponent>(slime, out var stc);
            var st = br.State.ToString() + (stc != null ? "/" + stc.Status : "") + (br.Ignored.Count > 0 ? "/ign" : "");
            if (states.Count == 0 || states[^1] != st) states.Add(st);
            latched = Server.EntMan.HasComponent<XenoSlimeLatchedComponent>(slime);
        }
        TestContext.Out.WriteLine("states: " + string.Join(" > ", states));
        Assert.That(latched, "slime should latch onto the mouse");
        Assert.That(Server.EntMan.GetComponent<XenoSlimeVictimComponent>(mouse).Slime, Is.EqualTo(slime));

        var h0 = Server.EntMan.GetComponent<XenoSlimeComponent>(slime).Hunger;
        await RunSeconds(10);
        var h1 = Server.EntMan.GetComponent<XenoSlimeComponent>(slime).Hunger;
        var mouseDead = Server.System<MobStateSystem>().IsDead(mouse);
        TestContext.Out.WriteLine($"hunger {h0} -> {h1}, mouse dead {mouseDead}, slime latched {Server.EntMan.HasComponent<XenoSlimeLatchedComponent>(slime)}, parent {Server.EntMan.ToPrettyString(Server.EntMan.GetComponent<TransformComponent>(slime).ParentUid)}");
        Assert.That(h1, Is.GreaterThan(h0));
        Assert.That(mouseDead, "mouse should die");
        Assert.That(Server.EntMan.HasComponent<XenoSlimeLatchedComponent>(slime), Is.False, "let go after death");
        Assert.That(Server.EntMan.HasComponent<XenoSlimeVictimComponent>(mouse), Is.False);
    }

    [Test]
    public async Task FriendsAreSafeTest()
    {
        var map = await Map();
        EntityUid slime = default, mouse = default;
        await Server.WaitAssertion(() =>
        {
            slime = SpawnSlime(map, "MobXenoSlimeGrey", 0, 100);
            mouse = Server.EntMan.SpawnAtPosition("MobMouse", map.GridCoords.Offset(new System.Numerics.Vector2(1f, 0)));
            Server.EntMan.RemoveComponent<Content.Server.NPC.HTN.HTNComponent>(mouse);
            Server.EntMan.GetComponent<XenoSlimeComponent>(slime).Friends.Add(mouse);
        });
        await RunSeconds(8);
        Assert.That(Server.EntMan.HasComponent<XenoSlimeLatchedComponent>(slime), Is.False);

        // Rabid ignores friendship (spec 5.4)
        await Server.WaitAssertion(() =>
            Server.System<XenoSlimeHungerSystem>().SetHunger((slime, Server.EntMan.GetComponent<XenoSlimeComponent>(slime)), 20));
        await RunSeconds(8);
        Assert.That(Server.EntMan.HasComponent<XenoSlimeLatchedComponent>(slime) || Server.System<MobStateSystem>().IsDead(mouse),
            "rabid slime should attack its friend");
    }

    [Test]
    public async Task OneSlimePerVictimTest()
    {
        var map = await Map();
        EntityUid a = default, b = default, human = default;
        await Server.WaitAssertion(() =>
        {
            human = Server.EntMan.SpawnAtPosition("MobHuman", map.GridCoords);
            a = SpawnSlime(map, "MobXenoSlimeGrey", 1f, 100);
            b = SpawnSlime(map, "MobXenoSlimeOrange", -1f, 100);
        });
        await RunSeconds(10);
        var la = Server.EntMan.HasComponent<XenoSlimeLatchedComponent>(a);
        var lb = Server.EntMan.HasComponent<XenoSlimeLatchedComponent>(b);
        TestContext.Out.WriteLine($"a latched {la}, b latched {lb}, b state {Server.EntMan.GetComponent<XenoSlimeBrainComponent>(b).State}");
        Assert.That(la ^ lb, "exactly one slime should be stuck on the human");
    }

    [Test]
    public async Task PullAndResistTest()
    {
        var map = await Map();
        EntityUid slime = default, human = default, helper = default;
        await Server.WaitAssertion(() =>
        {
            human = Server.EntMan.SpawnAtPosition("MobHuman", map.GridCoords);
            helper = Server.EntMan.SpawnAtPosition("MobHuman", map.GridCoords.Offset(new System.Numerics.Vector2(0, 1)));
            slime = SpawnSlime(map, "MobXenoSlimeGrey", 1f, 50);   // Angry, room to eat without splitting
        });
        await RunSeconds(8);
        Assert.That(Server.EntMan.HasComponent<XenoSlimeLatchedComponent>(slime), "should latch first");

        // Resist: the victim does the 5-second do-after
        await Server.WaitAssertion(() =>
        {
            var args = new Content.Shared.DoAfter.DoAfterArgs(Server.EntMan, human, 5f,
                new XenoSlimeResistDoAfterEvent(), slime, target: slime) { BreakOnDamage = false };
            Assert.That(Server.System<Content.Shared.DoAfter.SharedDoAfterSystem>().TryStartDoAfter(args));
        });
        await RunSeconds(4);
        Assert.That(Server.EntMan.HasComponent<XenoSlimeLatchedComponent>(slime), "still on after 4s");
        await RunSeconds(1.5f);
        Assert.That(Server.EntMan.HasComponent<XenoSlimeLatchedComponent>(slime), Is.False, "off after 5s");
        var parent = Server.EntMan.GetComponent<TransformComponent>(slime).ParentUid;
        Assert.That(parent, Is.Not.EqualTo(human), "dropped on the floor");

        // Let it latch again, then pull it off
        var trace = new System.Collections.Generic.List<string>();
        for (var i = 0; i < 80; i++)
        {
            await RunSeconds(0.1f);
            var br = Server.EntMan.GetComponent<XenoSlimeBrainComponent>(slime);
            var st = $"{br.State}{(br.Ignored.Count > 0 ? "/ign" : "")}";
            if (trace.Count == 0 || trace[^1] != st) trace.Add(st);
            if (Server.EntMan.HasComponent<XenoSlimeLatchedComponent>(slime)) break;
        }
        TestContext.Out.WriteLine("after resist: " + string.Join(" > ", trace) + $" | hunger {Server.EntMan.GetComponent<XenoSlimeComponent>(slime).Hunger} mood {Server.EntMan.GetComponent<XenoSlimeComponent>(slime).Mood} human state {Server.EntMan.GetComponent<Content.Shared.Mobs.Components.MobStateComponent>(human).CurrentState}");
        Assert.That(Server.EntMan.HasComponent<XenoSlimeLatchedComponent>(slime), "latches again");
        await Server.WaitAssertion(() =>
            Assert.That(Server.System<Content.Shared.Movement.Pulling.Systems.PullingSystem>().TryStartPull(helper, slime)));
        await RunSeconds(0.5f);
        Assert.That(Server.EntMan.HasComponent<XenoSlimeLatchedComponent>(slime), Is.False, "pulling unlatches");
    }

    [Test]
    public async Task FullLetsGoAndSplitsTest()
    {
        var map = await Map();
        EntityUid slime = default, human = default;
        await Server.WaitAssertion(() =>
        {
            human = Server.EntMan.SpawnAtPosition("MobHuman", map.GridCoords);
            slime = SpawnSlime(map, "MobXenoSlimeGrey", 1f, 140);
        });
        await RunSeconds(8);
        await RunSeconds(5);
        TestContext.Out.WriteLine($"slime deleted {Server.EntMan.Deleted(slime)}, slimes {CountSlimes()}, human victim comp {Server.EntMan.HasComponent<XenoSlimeVictimComponent>(human)}");
        Assert.That(Server.EntMan.Deleted(slime), "should have reached 150 and split");
        // A baby may have latched on afterwards (that's correct behaviour), but never the deleted parent
        if (Server.EntMan.TryGetComponent<XenoSlimeVictimComponent>(human, out var v))
            Assert.That(v.Slime, Is.Not.EqualTo(slime));
    }
}
