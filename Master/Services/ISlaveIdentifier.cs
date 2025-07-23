namespace Master.Services;

public interface ISlaveIdentifier
{
    string MasterUrl { get; }
    string SlaveId { get; }
}
