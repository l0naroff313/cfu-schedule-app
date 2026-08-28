using System.Reflection;

namespace UniversitySchedule.Infrastructure;

public static class InfrastructureAssembly
{
    public static Assembly Reference { get; } = typeof(InfrastructureAssembly).Assembly;
}
