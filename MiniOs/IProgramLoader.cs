namespace MiniOS
{
    public interface IProgramLoader : IProgramRunner
    {
        int SpawnByPath(string path, ProcessStartOptions? options = null);
    }
}
