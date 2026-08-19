namespace JorgenHoc.DataAccess.EfCoreManyToMany;

/// <summary>
/// The article's explicit-join-entity trio: the junction table carries payload
/// (<see cref="StudentCourse.EnrolledAt"/>, <see cref="StudentCourse.FinalGrade"/>), so
/// it needs a real entity class — plus skip navigations for when the payload is not needed.
/// </summary>
public class Student
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }

    // Can navigate directly to Courses (skipping the join entity)
    public List<Course> Courses { get; set; } = [];

    // Or navigate through the join entity (when you need the payload)
    public List<StudentCourse> StudentCourses { get; set; } = [];
}
