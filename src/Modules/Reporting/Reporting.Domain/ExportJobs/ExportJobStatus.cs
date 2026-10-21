namespace Reporting.Domain;

public enum ExportJobStatus
{
    Queued = 1,
    Running = 2,
    Completed = 3,
    Failed = 4,
    Expired = 5,
}
