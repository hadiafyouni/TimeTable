using System;
using System.Collections.Generic;
using System.Linq;

namespace Schedule
{
    // 1. We create a small helper class to represent individual "Name Cards"
    public class PlacementTask
    {
        public ClassRequirements Req { get; set; }
        public bool IsDoublePeriod { get; set; }
    }

    public class TimetableEngine
    {
        private readonly List<TimeSlot> _allSlots;
        private readonly Dictionary<int, List<TimeSlot>> _slotsByDay;

        public TimetableEngine(List<TimeSlot> slots, int seed = 0)
        {
            _allSlots = slots;
            _slotsByDay = slots.GroupBy(s => s.Day).ToDictionary(g => g.Key, g => g.OrderBy(s => s.Period).ToList());
        }

        public List<SchedulePlacement> GenerateSchedule(List<ClassRequirements> requirements)
        {
            var schedule = new List<SchedulePlacement>();
            var teacherSlotUsed = new HashSet<(int teacherId, int slotId)>();
            var classSlotUsed = new HashSet<(int classId, int slotId)>();
            var teacherHours = new Dictionary<int, int>();
            var classSubjectDayCount = new Dictionary<(int classId, int subjectId, int day), int>();

            // Order the VIPs: consecutive first, then most hours
            var requirements_ordered = requirements
                .OrderByDescending(r => r.RequiresConsecutive)
                .ThenByDescending(r => r.RequiredHours)
                .ToList();

            // FLATTENING: Break bulk requirements into individual lessons to schedule
            var tasks = new List<PlacementTask>();
            foreach (var req in requirements_ordered)
            {
                int hours = req.RequiredHours;
                if (req.RequiresConsecutive)
                {
                    // Add pairs
                    for (int i = 0; i < hours / 2; i++) 
                        tasks.Add(new PlacementTask { Req = req, IsDoublePeriod = true });
                    
                    // If they have an odd number of hours (e.g., 5), add the leftover as a single
                    if (hours % 2 != 0) 
                        tasks.Add(new PlacementTask { Req = req, IsDoublePeriod = false });
                }
                else
                {
                    // Add singles
                    for (int i = 0; i < hours; i++) 
                        tasks.Add(new PlacementTask { Req = req, IsDoublePeriod = false });
                }
            }

            // START THE ENGINE
            bool success = SolveCSP(0, tasks, schedule, teacherSlotUsed, classSlotUsed, teacherHours, classSubjectDayCount);

            if (!success)
            {
                throw new Exception("Algorithm explored all possible combinations. These constraints are impossible to fulfill.");
            }

            return schedule;
        }

        // 2. THE CSP ENGINE (Recursive Backtracking with Forward Checking)
        private bool SolveCSP(
            int taskIndex,
            List<PlacementTask> tasks,
            List<SchedulePlacement> schedule,
            HashSet<(int, int)> teacherSlotUsed,
            HashSet<(int, int)> classSlotUsed,
            Dictionary<int, int> teacherHours,
            Dictionary<(int, int, int), int> classSubjectDayCount)
        {
            // Base Case: We placed all 360 name cards! We win!
            if (taskIndex >= tasks.Count) return true;

            var task = tasks[taskIndex];
            var req = task.Req;

            if (task.IsDoublePeriod)
            {
                foreach (var day in _slotsByDay.Keys.OrderBy(d => d))
                {
                    var daySlots = _slotsByDay[day];
                    for (int i = 0; i < daySlots.Count - 1; i++)
                    {
                        var s1 = daySlots[i];
                        var s2 = daySlots[i + 1];

                        // FORWARD CHECKING ("The Sticky Note"): 
                        // Look at the slots BEFORE placing to see if they are valid.
                        if (CanPlace(req, s1, teacherSlotUsed, classSlotUsed, teacherHours, classSubjectDayCount) &&
                            CanPlace(req, s2, teacherSlotUsed, classSlotUsed, teacherHours, classSubjectDayCount) &&
                            !classSlotUsed.Contains((req.ClassId, s2.Id)) &&
                            !teacherSlotUsed.Contains((req.TeacherId, s2.Id)))
                        {
                            // Place the lesson (Sit the guest down)
                            PlaceLesson(req, s1, schedule, teacherSlotUsed, classSlotUsed, teacherHours, classSubjectDayCount);
                            PlaceLesson(req, s2, schedule, teacherSlotUsed, classSlotUsed, teacherHours, classSubjectDayCount);

                            // RECURSION: Try to seat the NEXT guest in the list
                            if (SolveCSP(taskIndex + 1, tasks, schedule, teacherSlotUsed, classSlotUsed, teacherHours, classSubjectDayCount))
                                return true; // It worked! Keep passing the success back up the chain.

                            // BACKTRACK: The future guests hit a dead end. 
                            // This placement caused a crash later on. Undo it ("Guest, please get up").
                            RemoveLesson(req, s1, schedule, teacherSlotUsed, classSlotUsed, teacherHours, classSubjectDayCount);
                            RemoveLesson(req, s2, schedule, teacherSlotUsed, classSlotUsed, teacherHours, classSubjectDayCount);
                        }
                    }
                }
            }
            else // Single Period
            {
                foreach (var day in _slotsByDay.Keys.OrderBy(d => d))
                {
                    var daySlots = _slotsByDay[day];
                    foreach (var slot in daySlots)
                    {
                        // FORWARD CHECKING
                        if (CanPlace(req, slot, teacherSlotUsed, classSlotUsed, teacherHours, classSubjectDayCount))
                        {
                            PlaceLesson(req, slot, schedule, teacherSlotUsed, classSlotUsed, teacherHours, classSubjectDayCount);

                            // RECURSION
                            if (SolveCSP(taskIndex + 1, tasks, schedule, teacherSlotUsed, classSlotUsed, teacherHours, classSubjectDayCount))
                                return true;

                            // BACKTRACK
                            RemoveLesson(req, slot, schedule, teacherSlotUsed, classSlotUsed, teacherHours, classSubjectDayCount);
                        }
                    }
                }
            }

            // If we loop through every single slot in the week and NONE of them work, 
            // return false. This triggers the previous guest to backtrack.
            return false;
        }

        // -- YOUR EXISTING METHODS STAY EXACTLY THE SAME --
        private bool CanPlace(ClassRequirements req, TimeSlot slot, HashSet<(int, int)> teacherSlotUsed, HashSet<(int, int)> classSlotUsed, Dictionary<int, int> teacherHours, Dictionary<(int, int, int), int> classSubjectDayCount)
        {
            if (teacherSlotUsed.Contains((req.TeacherId, slot.Id))) return false;
            if (classSlotUsed.Contains((req.ClassId, slot.Id))) return false;
            if (teacherHours.GetValueOrDefault(req.TeacherId) >= 25) return false;

            var key = (req.ClassId, req.SubjectId, slot.Day);
            var dayCount = classSubjectDayCount.GetValueOrDefault(key, 0);

            if (!req.RequiresConsecutive && dayCount >= 1) return false;
            if (req.RequiresConsecutive && dayCount >= 2) return false;

            return true;
        }

        private void PlaceLesson(ClassRequirements req, TimeSlot slot, List<SchedulePlacement> schedule, HashSet<(int, int)> teacherSlotUsed, HashSet<(int, int)> classSlotUsed, Dictionary<int, int> teacherHours, Dictionary<(int, int, int), int> classSubjectDayCount)
        {
            schedule.Add(new SchedulePlacement { ClassId = req.ClassId, SlotId = slot.Id, TeacherId = req.TeacherId, SubjectId = req.SubjectId });
            teacherSlotUsed.Add((req.TeacherId, slot.Id));
            classSlotUsed.Add((req.ClassId, slot.Id));
            teacherHours[req.TeacherId] = teacherHours.GetValueOrDefault(req.TeacherId) + 1;

            var dayKey = (req.ClassId, req.SubjectId, slot.Day);
            classSubjectDayCount[dayKey] = classSubjectDayCount.GetValueOrDefault(dayKey, 0) + 1;
        }

        // 3. THE UNDO BUTTON (Required for Backtracking)
        private void RemoveLesson(ClassRequirements req, TimeSlot slot, List<SchedulePlacement> schedule, HashSet<(int, int)> teacherSlotUsed, HashSet<(int, int)> classSlotUsed, Dictionary<int, int> teacherHours, Dictionary<(int, int, int), int> classSubjectDayCount)
        {
            // Remove from the main schedule list
            var placement = schedule.First(p => p.ClassId == req.ClassId && p.SlotId == slot.Id);
            schedule.Remove(placement);

            // Remove all the "sticky notes" so this slot can be used by someone else
            teacherSlotUsed.Remove((req.TeacherId, slot.Id));
            classSlotUsed.Remove((req.ClassId, slot.Id));
            teacherHours[req.TeacherId]--;

            var dayKey = (req.ClassId, req.SubjectId, slot.Day);
            classSubjectDayCount[dayKey]--;
        }
    }
}