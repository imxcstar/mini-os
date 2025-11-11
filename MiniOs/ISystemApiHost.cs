namespace MiniOS
{
    /// <summary>
    /// Allows infrastructure components to interact with the system API implementation without knowing its concrete type.
    /// </summary>
    public interface ISystemApiHost
    {
        void AttachRunner(IProgramRunner runner);
    }
}
