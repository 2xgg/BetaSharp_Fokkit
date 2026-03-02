namespace BetaSharp.Server.Threading;

public class RunServerThread
{
    private readonly Thread _thread;

    public RunServerThread(MinecraftServer server, string name)
    {
        _thread = new Thread(server.run) { Name = name };
    }

    public void Start() => _thread.Start();
}
