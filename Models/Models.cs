namespace Schedule
{
    public class TimeSlot
    {
        public int Id { get; set; }
        public int Day { get; set; }
        public int Period { get; set; }
    }

    public class ClassRequirements
    {
        public int ClassId { get; set; }
        public int SubjectId { get; set; }
        public int TeacherId { get; set; }
        public int RequiredHours { get; set; }
        public bool RequiresConsecutive { get; set; }
    }

    public class SchedulePlacement
    {
        public int ClassId { get; set; }
        public int SlotId { get; set; }
        public int TeacherId { get; set; }
        public int SubjectId { get; set; }
    }
}