namespace Reporting.Domain;

public static class ExportErrors
{
    public static readonly DomainError TypeInvalid = new("reporting.export_job.type_invalid", "Export type must be a lowercase dotted code such as 'orders.csv'.");
    public static readonly DomainError NotQueued = new("reporting.export_job.not_queued", "Only a queued export can be started.");
    public static readonly DomainError NotRunning = new("reporting.export_job.not_running", "Only a running export can be completed or failed.");
    public static readonly DomainError FileRequired = new("reporting.export_job.file_required", "An export cannot be completed without its file.");
    public static readonly DomainError FailureReasonInvalid = new("reporting.export_job.failure_reason_invalid", "A failure reason is required and limited to 500 characters.");
    public static readonly DomainError NotCompleted = new("reporting.export_job.not_completed", "Only a completed export can expire.");
    public static readonly DomainError AlreadyFailed = new("reporting.export_job.already_failed", "A failed export cannot be changed.");
    public static readonly DomainError AlreadyExpired = new("reporting.export_job.already_expired", "An expired export cannot be changed.");
}
