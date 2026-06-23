using API.Schema.ActionsContext.Actions;
using API.Schema.ActionsContext.Actions.Generic;
using Microsoft.EntityFrameworkCore;

namespace API.Schema.ActionsContext;

public class ActionsContext(DbContextOptions<ActionsContext> options) : KenkuBaseContext<ActionsContext>(options)
{
    public DbSet<ActionRecord> Actions  { get; set; }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ActionRecord>()
            .HasDiscriminator(a => a.Action)
            .HasValue<ChapterDownloadedActionRecord>(Schema.ActionsContext.Actions.Actions.ChapterDownloaded)
            .HasValue<CoverDownloadedActionRecord>(Schema.ActionsContext.Actions.Actions.CoverDownloaded)
            .HasValue<ChaptersRetrievedActionRecord>(Schema.ActionsContext.Actions.Actions.ChaptersRetrieved)
            .HasValue<MetadataUpdatedActionRecord>(Schema.ActionsContext.Actions.Actions.MetadataUpdated)
            .HasValue<DataMovedActionRecord>(Schema.ActionsContext.Actions.Actions.DataMoved)
            .HasValue<LibraryMovedActionRecord>(Schema.ActionsContext.Actions.Actions.LibraryMoved)
            .HasValue<StartupActionRecord>(Schema.ActionsContext.Actions.Actions.Startup);

        modelBuilder.Entity<ChapterDownloadedActionRecord>().Property(a => a.SeriesId).HasColumnName("SeriesId");
        modelBuilder.Entity<ChapterDownloadedActionRecord>().Property(a => a.ChapterId).HasColumnName("ChapterId");
        
        modelBuilder.Entity<CoverDownloadedActionRecord>().Property(a => a.SeriesId).HasColumnName("SeriesId");
        
        modelBuilder.Entity<ChaptersRetrievedActionRecord>().Property(a => a.SeriesId).HasColumnName("SeriesId");
        
        modelBuilder.Entity<MetadataUpdatedActionRecord>().Property(a => a.SeriesId).HasColumnName("SeriesId");
        
        modelBuilder.Entity<LibraryMovedActionRecord>().Property(a => a.SeriesId).HasColumnName("SeriesId");
    }

    public IQueryable<ActionRecord> FilterActionsSeries(string SeriesId) => this.Actions
        .FromSqlInterpolated($"""SELECT * FROM public."Actions" WHERE "SeriesId" = {SeriesId}""");

    public IQueryable<ActionRecord> FilterActionsChapter(string ChapterId) => this.Actions
        .FromSqlInterpolated($"""SELECT * FROM public."Actions" WHERE "ChapterId" = {ChapterId}""");
    
    public IQueryable<ActionRecord> FilterActionsSeriesAndChapter(string SeriesId, string ChapterId) => this.Actions
        .FromSqlInterpolated($"""SELECT * FROM public."Actions" WHERE "SeriesId" = {SeriesId} AND "ChapterId" = {ChapterId}""");

    public IQueryable<ActionRecord> FilterActions(string? SeriesId, string? ChapterId)
    {
        if (SeriesId is { } seriesId && ChapterId is { } chapterId)
            return FilterActionsSeriesAndChapter(seriesId, chapterId);
        if (SeriesId is { } mangaId2)
            return FilterActionsSeries(mangaId2);
        if (ChapterId is { } chapterId2)
            return FilterActionsChapter(chapterId2);
        return this.Actions.AsQueryable();
    }
}