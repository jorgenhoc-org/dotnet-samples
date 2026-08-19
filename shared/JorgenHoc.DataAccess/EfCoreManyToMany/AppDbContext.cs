using Microsoft.EntityFrameworkCore;

namespace JorgenHoc.DataAccess.EfCoreManyToMany;

/// <summary>
/// Context for the "EF Core many-to-many relationships" article. Named
/// <c>AppDbContext</c> to match the article text — the namespace keeps it distinct from
/// the other articles' contexts.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Post> Posts => Set<Post>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<Student> Students => Set<Student>();
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<StudentCourse> StudentCourses => Set<StudentCourse>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Own SQL schema per article — matches samples/ef-core-many-to-many/seed.sql.
        modelBuilder.HasDefaultSchema("ManyToMany");

        // Post <-> Tag stays implicit on purpose: no configuration at all. EF Core
        // creates the PostTag junction table (columns PostsId, TagsId) by convention,
        // which the sample proves by reading the table's columns back from the database.

        // The article's Fluent API for the explicit join entity, verbatim.
        modelBuilder.Entity<StudentCourse>(entity =>
        {
            entity.HasKey(sc => new { sc.StudentId, sc.CourseId }); // Composite PK

            entity.HasOne(sc => sc.Student)
                  .WithMany(s => s.StudentCourses)
                  .HasForeignKey(sc => sc.StudentId);

            entity.HasOne(sc => sc.Course)
                  .WithMany(c => c.StudentCourses)
                  .HasForeignKey(sc => sc.CourseId);

            entity.Property(sc => sc.EnrolledAt)
                  .HasDefaultValueSql("GETUTCDATE()");
        });

        // Configure skip navigations (direct Student.Courses access)
        modelBuilder.Entity<Student>()
            .HasMany(s => s.Courses)
            .WithMany(c => c.Students)
            .UsingEntity<StudentCourse>();
    }
}
