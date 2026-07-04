namespace FileCrawler.Models;

/// <summary>Progress reported while a crawl is running.</summary>
public sealed record CrawlProgress(string RootPath, int NodesSoFar);
