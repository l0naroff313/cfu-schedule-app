using Microsoft.EntityFrameworkCore;
using UniversitySchedule.Domain.Catalog;
using UniversitySchedule.Domain.Identity;
using UniversitySchedule.Domain.PersonalData;
using UniversitySchedule.Domain.Scheduling;

namespace UniversitySchedule.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Installation> Installations => Set<Installation>();

    public DbSet<SyncedNote> Notes => Set<SyncedNote>();

    public DbSet<SyncedAssignment> Assignments => Set<SyncedAssignment>();

    public DbSet<PersonalDataMutationReceipt> PersonalDataMutationReceipts =>
        Set<PersonalDataMutationReceipt>();

    public DbSet<ReferenceCatalogDocument> ReferenceCatalogDocuments =>
        Set<ReferenceCatalogDocument>();

    public DbSet<ReferenceCatalogImportLog> ReferenceCatalogImportLogs =>
        Set<ReferenceCatalogImportLog>();

    public DbSet<ScheduleSourceDocument> ScheduleSourceDocuments =>
        Set<ScheduleSourceDocument>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Installation>(entity =>
        {
            entity.ToTable("installations");
            entity.HasKey(installation => installation.Id);

            entity.Property(installation => installation.Id)
                .HasColumnName("id")
                .ValueGeneratedNever();
            entity.Property(installation => installation.SecretHash)
                .HasColumnName("secret_hash")
                .HasMaxLength(32)
                .IsRequired();
            entity.Property(installation => installation.Platform)
                .HasColumnName("platform")
                .HasMaxLength(16)
                .IsRequired();
            entity.Property(installation => installation.AppVersion)
                .HasColumnName("app_version")
                .HasMaxLength(64)
                .IsRequired();
            entity.Property(installation => installation.CreatedAtUtc)
                .HasColumnName("created_at_utc")
                .HasColumnType("timestamp with time zone");
            entity.Property(installation => installation.LastSeenAtUtc)
                .HasColumnName("last_seen_at_utc")
                .HasColumnType("timestamp with time zone");
            entity.Property(installation => installation.RevokedAtUtc)
                .HasColumnName("revoked_at_utc")
                .HasColumnType("timestamp with time zone");

            entity.Ignore(installation => installation.IsRevoked);
            entity.HasIndex(installation => installation.LastSeenAtUtc)
                .HasDatabaseName("ix_installations_last_seen_at_utc");
        });

        modelBuilder.Entity<SyncedNote>(entity =>
        {
            entity.ToTable("notes");
            entity.HasKey(note => new { note.InstallationId, note.Id });
            entity.Property(note => note.InstallationId).HasColumnName("installation_id");
            entity.Property(note => note.Id).HasColumnName("id");
            entity.Property(note => note.LastMutationId).HasColumnName("last_mutation_id");
            entity.Property(note => note.LessonId).HasColumnName("lesson_id");
            entity.Property(note => note.Text).HasColumnName("text").HasMaxLength(8_000).IsRequired();
            entity.Property(note => note.Title).HasColumnName("title").HasMaxLength(200);
            entity.Property(note => note.Subject).HasColumnName("subject").HasMaxLength(200);
            entity.Property(note => note.IsPinned).HasColumnName("is_pinned");
            entity.Property(note => note.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamp with time zone");
            entity.Property(note => note.ClientUpdatedAtUtc).HasColumnName("client_updated_at_utc").HasColumnType("timestamp with time zone");
            entity.Property(note => note.ServerUpdatedAtUtc).HasColumnName("server_updated_at_utc").HasColumnType("timestamp with time zone");
            entity.Property(note => note.DeletedAtUtc).HasColumnName("deleted_at_utc").HasColumnType("timestamp with time zone");
            entity.Property(note => note.Revision).HasColumnName("revision");
            entity.Ignore(note => note.IsDeleted);
            entity.HasOne<Installation>()
                .WithMany()
                .HasForeignKey(note => note.InstallationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(note => new { note.InstallationId, note.ServerUpdatedAtUtc })
                .HasDatabaseName("ix_notes_installation_server_updated");
        });

        modelBuilder.Entity<SyncedAssignment>(entity =>
        {
            entity.ToTable("assignments");
            entity.HasKey(assignment => new { assignment.InstallationId, assignment.Id });
            entity.Property(assignment => assignment.InstallationId).HasColumnName("installation_id");
            entity.Property(assignment => assignment.Id).HasColumnName("id");
            entity.Property(assignment => assignment.LastMutationId).HasColumnName("last_mutation_id");
            entity.Property(assignment => assignment.LessonId).HasColumnName("lesson_id");
            entity.Property(assignment => assignment.Subject).HasColumnName("subject").HasMaxLength(200).IsRequired();
            entity.Property(assignment => assignment.Text).HasColumnName("text").HasMaxLength(8_000).IsRequired();
            entity.Property(assignment => assignment.DeadlineUtc).HasColumnName("deadline_utc").HasColumnType("timestamp with time zone");
            entity.Property(assignment => assignment.Status).HasColumnName("status").HasConversion<int>();
            entity.Property(assignment => assignment.CreatedAtUtc).HasColumnName("created_at_utc").HasColumnType("timestamp with time zone");
            entity.Property(assignment => assignment.ClientUpdatedAtUtc).HasColumnName("client_updated_at_utc").HasColumnType("timestamp with time zone");
            entity.Property(assignment => assignment.ServerUpdatedAtUtc).HasColumnName("server_updated_at_utc").HasColumnType("timestamp with time zone");
            entity.Property(assignment => assignment.DeletedAtUtc).HasColumnName("deleted_at_utc").HasColumnType("timestamp with time zone");
            entity.Property(assignment => assignment.Revision).HasColumnName("revision");
            entity.Ignore(assignment => assignment.IsDeleted);
            entity.HasOne<Installation>()
                .WithMany()
                .HasForeignKey(assignment => assignment.InstallationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(assignment => new { assignment.InstallationId, assignment.ServerUpdatedAtUtc })
                .HasDatabaseName("ix_assignments_installation_server_updated");
        });

        modelBuilder.Entity<PersonalDataMutationReceipt>(entity =>
        {
            entity.ToTable("personal_data_mutation_receipts");
            entity.HasKey(receipt => new { receipt.InstallationId, receipt.MutationId });
            entity.Property(receipt => receipt.InstallationId).HasColumnName("installation_id");
            entity.Property(receipt => receipt.MutationId).HasColumnName("mutation_id");
            entity.Property(receipt => receipt.EntityKind).HasColumnName("entity_kind").HasConversion<int>();
            entity.Property(receipt => receipt.EntityId).HasColumnName("entity_id");
            entity.Property(receipt => receipt.Outcome).HasColumnName("outcome").HasConversion<int>();
            entity.Property(receipt => receipt.ProcessedAtUtc)
                .HasColumnName("processed_at_utc")
                .HasColumnType("timestamp with time zone");
            entity.HasOne<Installation>()
                .WithMany()
                .HasForeignKey(receipt => receipt.InstallationId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(receipt => receipt.ProcessedAtUtc)
                .HasDatabaseName("ix_mutation_receipts_processed_at_utc");
        });

        modelBuilder.Entity<ReferenceCatalogDocument>(entity =>
        {
            entity.ToTable("reference_catalog_documents");
            entity.HasKey(document => document.Id);
            entity.Property(document => document.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(document => document.PayloadJson)
                .HasColumnName("payload_json")
                .HasColumnType("jsonb")
                .IsRequired();
            entity.Property(document => document.ContentHash)
                .HasColumnName("content_hash")
                .HasMaxLength(64)
                .IsRequired();
            entity.Property(document => document.SchemaVersion).HasColumnName("schema_version");
            entity.Property(document => document.SourceGeneratedAtUtc)
                .HasColumnName("source_generated_at_utc")
                .HasColumnType("timestamp with time zone");
            entity.Property(document => document.ImportedAtUtc)
                .HasColumnName("imported_at_utc")
                .HasColumnType("timestamp with time zone");
            entity.HasIndex(document => document.ContentHash)
                .HasDatabaseName("ix_reference_catalog_content_hash");
        });

        modelBuilder.Entity<ReferenceCatalogImportLog>(entity =>
        {
            entity.ToTable("reference_catalog_import_logs");
            entity.HasKey(log => log.Id);
            entity.Property(log => log.Id).HasColumnName("id").ValueGeneratedNever();
            entity.Property(log => log.StartedAtUtc)
                .HasColumnName("started_at_utc")
                .HasColumnType("timestamp with time zone");
            entity.Property(log => log.FinishedAtUtc)
                .HasColumnName("finished_at_utc")
                .HasColumnType("timestamp with time zone");
            entity.Property(log => log.Status).HasColumnName("status").HasConversion<int>();
            entity.Property(log => log.ContentHash)
                .HasColumnName("content_hash")
                .HasMaxLength(64);
            entity.Property(log => log.ProgramCount).HasColumnName("program_count");
            entity.Property(log => log.GroupCount).HasColumnName("group_count");
            entity.Property(log => log.TeacherCount).HasColumnName("teacher_count");
            entity.Property(log => log.ErrorMessage)
                .HasColumnName("error_message")
                .HasMaxLength(4_000);
            entity.HasIndex(log => log.StartedAtUtc)
                .HasDatabaseName("ix_reference_catalog_import_started_at");
        });

        modelBuilder.Entity<ScheduleSourceDocument>(entity =>
        {
            entity.ToTable("schedule_source_documents");
            entity.HasKey(document => document.Key);
            entity.Property(document => document.Key)
                .HasColumnName("key")
                .HasMaxLength(256);
            entity.Property(document => document.SourceUrl)
                .HasColumnName("source_url")
                .HasMaxLength(1_000)
                .IsRequired();
            entity.Property(document => document.PayloadJson)
                .HasColumnName("payload_json")
                .HasColumnType("jsonb")
                .IsRequired();
            entity.Property(document => document.FetchedAtUtc)
                .HasColumnName("fetched_at_utc")
                .HasColumnType("timestamp with time zone");
            entity.HasIndex(document => document.FetchedAtUtc)
                .HasDatabaseName("ix_schedule_source_documents_fetched_at");
        });
    }
}
