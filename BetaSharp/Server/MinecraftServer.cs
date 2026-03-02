using System.Diagnostics;
using System.Threading;
using BetaSharp.Network.Packets.S2CPlay;
using BetaSharp.Server.Commands;
using BetaSharp.Server.Entities;
using BetaSharp.Server.Internal;
using BetaSharp.Server.Network;
using BetaSharp.Server.Worlds;
using BetaSharp.Util.Maths;
using BetaSharp.Worlds;
using BetaSharp.Worlds.Storage;
using Microsoft.Extensions.Logging;

namespace BetaSharp.Server;

public abstract class MinecraftServer : CommandOutput
{
    public Dictionary<string, int> GIVE_COMMANDS_COOLDOWNS = [];
    public ConnectionListener connections;
    public IServerConfiguration config;
    public ServerWorld[] worlds;
    public PlayerManager playerManager;
    private ServerCommandHandler commandHandler;
    public bool running = true;
    public bool stopped;
    private int ticks;
    public string progressMessage;
    public int progress;
    private readonly List<Command> _pendingCommands = [];
    private readonly object _pendingCommandsLock = new();
    public EntityTracker[] entityTrackers = new EntityTracker[2];
    public bool onlineMode;
    public bool spawnAnimals;
    public bool pvpEnabled;
    public bool flightEnabled;
    protected bool logHelp = true;

    private readonly ILogger<MinecraftServer> _logger = Log.Instance.For<MinecraftServer>();
    private readonly Lock _tpsLock = new();
    private long _lastTpsTime;
    private int _ticksThisSecond;
    private float _currentTps;

    private volatile bool _isPaused;

    public float Tps
    {
        get
        {
            lock (_tpsLock)
            {
                return _currentTps;
            }
        }
    }

    public void SetPaused(bool paused)
    {
        _isPaused = paused;
    }

    public MinecraftServer(IServerConfiguration config)
    {
        this.config = config;
    }

    protected virtual bool Init()
    {
        commandHandler = new ServerCommandHandler(this);

        onlineMode = config.GetOnlineMode(true);
        spawnAnimals = config.GetSpawnAnimals(true);
        pvpEnabled = config.GetPvpEnabled(true);
        flightEnabled = config.GetAllowFlight(false);

        playerManager = CreatePlayerManager();
        entityTrackers[0] = new EntityTracker(this, 0);
        entityTrackers[1] = new EntityTracker(this, -1);
        long startTimestamp = Stopwatch.GetTimestamp();
        string worldName = config.GetLevelName("world");
        string seedString = config.GetLevelSeed("");
        long seed = Random.Shared.NextInt64();
        if (seedString.Length > 0)
        {
            try
            {
                seed = long.Parse(seedString);
            }
            catch (FormatException)
            {
                // Java-compatible string hashing
                int hash = 0;
                foreach (char c in seedString)
                {
                    hash = 31 * hash + c;
                }
                seed = hash;
            }
        }

        _logger.LogInformation($"Preparing level \"{worldName}\"");
        loadWorld(new RegionWorldStorageSource(GetFilePath(".")), worldName, seed);

        if (logHelp)
        {
            long elapsedNs = (long)((Stopwatch.GetTimestamp() - startTimestamp) * (1_000_000_000.0 / Stopwatch.Frequency));
            _logger.LogInformation($"Done ({elapsedNs}ns)! For help, type \"help\" or \"?\"");
        }

        return true;
    }

    private void loadWorld(IWorldStorageSource storageSource, string worldDir, long seed)
    {
        worlds = new ServerWorld[2];
        RegionWorldStorage worldStorage = new RegionWorldStorage(GetFilePath("."), worldDir, true);

        for (int i = 0; i < worlds.Length; i++)
        {
            if (i == 0)
            {
                worlds[i] = new ServerWorld(this, worldStorage, worldDir, i == 0 ? 0 : -1, seed);
            }
            else
            {
                worlds[i] = new ReadOnlyServerWorld(this, worldStorage, worldDir, i == 0 ? 0 : -1, seed, worlds[0]);
            }

            worlds[i].addWorldAccess(new ServerWorldEventListener(this, worlds[i]));
            worlds[i].difficulty = config.GetSpawnMonsters(true) ? 1 : 0;
            worlds[i].allowSpawning(config.GetSpawnMonsters(true), spawnAnimals);
            playerManager.saveAllPlayers(worlds);
        }

        short startRegionSize = 196;
        long lastTimeLogged = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
;

        for (int i = 0; i < worlds.Length; i++)
        {
            _logger.LogInformation($"Preparing start region for level {i}");
            if (i == 0 || config.GetAllowNether(true))
            {
                ServerWorld world = worlds[i];
                Vec3i spawnPos = world.getSpawnPos();

                for (int x = -startRegionSize; x <= startRegionSize && running; x += 16)
                {
                    for (int z = -startRegionSize; z <= startRegionSize && running; z += 16)
                    {
                        long currentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
;
                        if (currentTime < lastTimeLogged)
                        {
                            lastTimeLogged = currentTime;
                        }

                        if (currentTime > lastTimeLogged + 1000L)
                        {
                            int total = (startRegionSize * 2 + 1) * (startRegionSize * 2 + 1);
                            int complete = (x + startRegionSize) * (startRegionSize * 2 + 1) + z + 1;
                            logProgress("Preparing spawn area", complete * 100 / total);
                            lastTimeLogged = currentTime;
                        }

                        world.chunkCache.LoadChunk(spawnPos.X + x >> 4, spawnPos.Z + z >> 4);

                        while (world.doLightingUpdates() && running)
                        {
                        }
                    }
                }
            }
        }

        clearProgress();
    }

    private void logProgress(string progressType, int progress)
    {
        progressMessage = progressType;
        this.progress = progress;
        _logger.LogInformation($"{progressType}: {progress}%");
    }

    private void clearProgress()
    {
        progressMessage = null;
        progress = 0;
    }

    private void saveWorlds()
    {
        _logger.LogInformation("Saving chunks");

        foreach (ServerWorld world in worlds)
        {
            world.saveWithLoadingDisplay(true, null);
            world.forceSave();
        }
    }

    private void shutdown()
    {
        if (stopped)
        {
            return;
        }

        _logger.LogInformation("Stopping server");

        if (playerManager != null)
        {
            playerManager.savePlayers();
        }

        foreach (ServerWorld world in worlds)
        {
            if (world != null)
            {
                saveWorlds();
            }
        }
    }

    public void stop()
    {
        running = false;
    }

    public void run()
    {
        try
        {
            if (Init())
            {
                long lastTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
;
                long accumulatedTime = 0L;
                _lastTpsTime = lastTime;
                _ticksThisSecond = 0;

                while (running)
                {
                    long currentTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
;
                    long tickLength = currentTime - lastTime;
                    if (tickLength > 2000L)
                    {
                        _logger.LogWarning("Can't keep up! Did the system time change, or is the server overloaded?");
                        tickLength = 2000L;
                    }

                    if (tickLength < 0L)
                    {
                        _logger.LogWarning("Time ran backwards! Did the system time change?");
                        tickLength = 0L;
                    }

                    accumulatedTime += tickLength;
                    lastTime = currentTime;

                    if (_isPaused)
                    {
                        accumulatedTime = 0L;
                        lock (_tpsLock)
                        {
                            _currentTps = 0.0f;
                        }
                        continue;
                    }

                    if (worlds[0].canSkipNight())
                    {
                        tick();
                        _ticksThisSecond++;
                        accumulatedTime = 0L;
                    }
                    else
                    {
                        while (accumulatedTime > 50L)
                        {
                            accumulatedTime -= 50L;
                            tick();
                            _ticksThisSecond++;
                        }
                    }

                    long tpsNow = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
;
                    long tpsElapsed = tpsNow - _lastTpsTime;
                    if (tpsElapsed >= 1000L)
                    {
                        lock (_tpsLock)
                        {
                            _currentTps = _ticksThisSecond * 1000.0f / tpsElapsed;
                        }
                        _ticksThisSecond = 0;
                        _lastTpsTime = tpsNow;
                    }

                    Thread.Sleep(1);
                }
            }
            else
            {
                while (running)
                {
                    runPendingCommands();
                    Thread.Sleep(10);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception");

            while (running)
            {
                runPendingCommands();
                Thread.Sleep(10);
            }
        }
        finally
        {
            try
            {
                shutdown();
                stopped = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during shutdown");
            }
            finally
            {
                if (this is not InternalServer)
                {
                    Environment.Exit(0);
                }
            }
        }
    }

    private void tick()
    {
        List<string> completeCooldowns = [];

        foreach (string key in GIVE_COMMANDS_COOLDOWNS.Keys.ToList())
        {
            if (GIVE_COMMANDS_COOLDOWNS[key] > 0)
            {
                GIVE_COMMANDS_COOLDOWNS[key]--;
            }
            else
            {
                completeCooldowns.Add(key);
            }
        }

        foreach (string key in completeCooldowns)
        {
            GIVE_COMMANDS_COOLDOWNS.Remove(key);
        }

        ticks++;

        for (int i = 0; i < worlds.Length; i++)
        {
            if (i == 0 || config.GetAllowNether(true))
            {
                ServerWorld world = worlds[i];
                if (ticks % 20 == 0)
                {
                    playerManager.sendToDimension(new WorldTimeUpdateS2CPacket(world.getTime()), world.dimension.Id);
                }

                world.Tick();

                while (world.doLightingUpdates())
                {
                }

                world.tickEntities();
            }
        }

        if (connections != null)
        {
            connections.Tick();
        }
        playerManager.updateAllChunks();

        foreach (EntityTracker t in entityTrackers)
        {
            t.tick();
        }

        try
        {
            runPendingCommands();
        }
        catch (Exception e)
        {
            _logger.LogWarning($"Unexpected exception while parsing console command: {e}");
        }
    }

    public void queueCommands(string str, CommandOutput cmd)
    {
        lock (_pendingCommandsLock)
        {
            _pendingCommands.Add(new Command(str, cmd));
        }
    }

    public void runPendingCommands()
    {
        while (true)
        {
            Command cmd;
            lock (_pendingCommandsLock)
            {
                if (_pendingCommands.Count == 0) break;
                cmd = _pendingCommands[0];
                _pendingCommands.RemoveAt(0);
            }
            commandHandler.ExecuteCommand(cmd);
        }
    }

    public abstract string GetFilePath(string path);

    public void SendMessage(string message)
    {
        _logger.LogInformation(message);
    }

    public void Warn(string message)
    {
        _logger.LogWarning(message);
    }

    public string GetName()
    {
        return "CONSOLE";
    }

    public ServerWorld getWorld(int dimensionId)
    {
        return dimensionId == -1 ? worlds[1] : worlds[0];
    }

    public EntityTracker getEntityTracker(int dimensionId)
    {
        return dimensionId == -1 ? entityTrackers[1] : entityTrackers[0];
    }
    protected virtual PlayerManager CreatePlayerManager()
    {
        return new PlayerManager(this);
    }

}
