namespace FamilyResilience.Application.Analytics;

/// <summary>An export file opened for download. The caller owns <see cref="Content"/> and must dispose it.</summary>
public sealed record ExportDownload(Stream Content, string FileName);
