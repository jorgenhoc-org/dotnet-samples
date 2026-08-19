namespace JorgenHoc.DataAccess.EfCoreManyToMany;

public enum Grade { A, B, C, D, F }

// The join entity — has its own properties beyond just the FKs
public class StudentCourse
{
    public int StudentId { get; set; }
    public Student Student { get; set; } = null!;

    public int CourseId { get; set; }
    public Course Course { get; set; } = null!;

    // Additional payload
    public DateTime EnrolledAt { get; set; }
    public Grade? FinalGrade { get; set; }
}
