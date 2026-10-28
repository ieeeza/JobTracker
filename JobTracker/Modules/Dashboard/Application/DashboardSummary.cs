using JobTracker.Modules.JobApplications.Application;
using JobTracker.Modules.JobApplications.Domain;

namespace JobTracker.Modules.Dashboard.Application;

public sealed record Metric(string Label, int Value);
public sealed record ChartBar(string Label, int Count, double Width);
public sealed record BoardColumn(string Title, IReadOnlyList<JobInfo> Jobs);
public sealed record DashboardSummary(IReadOnlyList<Metric> Metrics, IReadOnlyList<ChartBar> Months,
    IReadOnlyList<ChartBar> Distribution, IReadOnlyList<JobInfo> Recent, IReadOnlyList<JobInfo> NextActions)
{
    public static DashboardSummary From(IReadOnlyList<JobInfo> all)
    {
        var jobs = all.Where(j => !j.IsDeleted).ToArray();
        Metric[] metrics = [new("Vagas salvas", jobs.Length), new("Candidaturas", jobs.Count(j => j.AppliedAt.HasValue)),
            new("Processos ativos", jobs.Count(j => j.Status is ApplicationStatus.Applied or ApplicationStatus.Screening or ApplicationStatus.Interview or ApplicationStatus.TechnicalTest)),
            new("Entrevistas", jobs.Count(j => j.Status == ApplicationStatus.Interview)),
            new("Propostas", jobs.Count(j => j.Status == ApplicationStatus.Offer)), new("Recusadas", jobs.Count(j => j.Status == ApplicationStatus.Rejected))];
        DateTime month = new(DateTime.Today.Year, DateTime.Today.Month, 1);
        var periods = Enumerable.Range(-5, 6).Select(i => month.AddMonths(i)).Select(m =>
            (Label: m.ToString("MMM/yy", System.Globalization.CultureInfo.GetCultureInfo("pt-BR")), Count: jobs.Count(j => j.AppliedAt?.ToLocalTime() >= m && j.AppliedAt?.ToLocalTime() < m.AddMonths(1)))).ToArray();
        var statuses = StatusCatalog.All.Select(s => (Label: s.Label, Count: jobs.Count(j => j.Status == s.Value))).ToArray();
        static ChartBar[] Bars((string Label, int Count)[] values) => values.Select(v => new ChartBar(v.Label, v.Count, 240d * v.Count / Math.Max(1, values.Max(x => x.Count)))).ToArray();
        return new(metrics, Bars(periods), Bars(statuses), jobs.OrderByDescending(j => j.UpdatedAt).Take(6).ToArray(),
            jobs.Where(j => !string.IsNullOrWhiteSpace(j.Content.NextAction)).OrderBy(j => j.Content.NextActionDate ?? DateTime.MaxValue).Take(12).ToArray());
    }
}
