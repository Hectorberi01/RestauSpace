using Reporting.Application;
using Reporting.Application.Abstractions;
using SaasRestaurant.BuildingBlocks.Infrastructure;

namespace Reporting.Infrastructure.Persistence;

internal sealed class ReportingUnitOfWork(ReportingDbContext context) : OutboxUnitOfWork<ReportingDbContext>(context), IReportingUnitOfWork
{
    protected override DomainError ConcurrentUpdate => ReportingApplicationErrors.ConcurrentUpdate;
}
