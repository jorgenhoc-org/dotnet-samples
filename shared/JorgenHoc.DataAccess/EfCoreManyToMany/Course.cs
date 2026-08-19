namespace JorgenHoc.DataAccess.EfCoreManyToMany;

public class Course
{
    public int Id { get; set; }
    public required string Title { get; set; }
    public int Credits { get; set; }

    public List<Student> Students { get; set; } = [];
    public List<StudentCourse> StudentCourses { get; set; } = [];
}
