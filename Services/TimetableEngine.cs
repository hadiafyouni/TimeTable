using System;
using System.Collections.Generic;
using System.Linq;

namespace Schedule
{
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

            // Track occupied slots per teacher and per class
            var teacherSlotUsed = new HashSet<(int teacherId, int slotId)>();
            var classSlotUsed = new HashSet<(int classId, int slotId)>();
            var teacherHours = new Dictionary<int, int>();
            // Track how many times a subject is scheduled per class per day
            var classSubjectDayCount = new Dictionary<(int classId, int subjectId, int day), int>();

            // Order: consecutive first, then most hours
            var requirements_ordered = requirements
                .OrderByDescending(r => r.RequiresConsecutive)
                .ThenByDescending(r => r.RequiredHours)
                .ToList();

            foreach (var req in requirements_ordered)
            {
                if (!teacherHours.ContainsKey(req.TeacherId))
                    teacherHours[req.TeacherId] = 0;

                int hoursLeft = req.RequiredHours;

                if (req.RequiresConsecutive)
                {
                    // Place in pairs (double periods) on different days
                    int pairsNeeded = hoursLeft / 2;
                    int placed = 0;

                    foreach (var day in _slotsByDay.Keys.OrderBy(d => d))
                    {
                        if (placed >= pairsNeeded) break;

                        var daySlots = _slotsByDay[day];

                        // Find two adjacent free slots
                        for (int i = 0; i < daySlots.Count - 1; i++)
                        {
                            var s1 = daySlots[i];
                            var s2 = daySlots[i + 1];

                            if (CanPlace(req, s1, teacherSlotUsed, classSlotUsed, teacherHours, classSubjectDayCount) &&
                                CanPlace(req, s2, teacherSlotUsed, classSlotUsed, teacherHours, classSubjectDayCount) &&
                                !classSlotUsed.Contains((req.ClassId, s2.Id)) &&
                                !teacherSlotUsed.Contains((req.TeacherId, s2.Id)))
                            {
                                PlaceLesson(req, s1, schedule, teacherSlotUsed, classSlotUsed, teacherHours, classSubjectDayCount);
                                PlaceLesson(req, s2, schedule, teacherSlotUsed, classSlotUsed, teacherHours, classSubjectDayCount);
                                placed++;
                                break;
                            }
                        }
                    }

                    if (placed < pairsNeeded)
                        throw new Exception($"Could not schedule consecutive subject {req.SubjectId} for class {req.ClassId}. Not enough adjacent slots available.");
                }
                else
                {
                    // Place one per day on different days
                    int placed = 0;

                    foreach (var day in _slotsByDay.Keys.OrderBy(d => d))
                    {
                        if (placed >= hoursLeft) break;

                        var daySlots = _slotsByDay[day];

                        foreach (var slot in daySlots)
                        {
                            if (CanPlace(req, slot, teacherSlotUsed, classSlotUsed, teacherHours, classSubjectDayCount))
                            {
                                PlaceLesson(req, slot, schedule, teacherSlotUsed, classSlotUsed, teacherHours, classSubjectDayCount);
                                placed++;
                                break;
                            }
                        }
                    }

                    if (placed < hoursLeft)
                        throw new Exception($"Could not schedule subject {req.SubjectId} for class {req.ClassId}. Only placed {placed}/{hoursLeft} hours. Teacher {req.TeacherId} may be overloaded.");
                }
            }

            return schedule;
        }

        private bool CanPlace(
            ClassRequirements req,
            TimeSlot slot,
            HashSet<(int, int)> teacherSlotUsed,
            HashSet<(int, int)> classSlotUsed,
            Dictionary<int, int> teacherHours,
            Dictionary<(int, int, int), int> classSubjectDayCount)
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

        private void PlaceLesson(
            ClassRequirements req,
            TimeSlot slot,
            List<SchedulePlacement> schedule,
            HashSet<(int, int)> teacherSlotUsed,
            HashSet<(int, int)> classSlotUsed,
            Dictionary<int, int> teacherHours,
            Dictionary<(int, int, int), int> classSubjectDayCount)
        {
            schedule.Add(new SchedulePlacement
            {
                ClassId = req.ClassId,
                SlotId = slot.Id,
                TeacherId = req.TeacherId,
                SubjectId = req.SubjectId
            });

            teacherSlotUsed.Add((req.TeacherId, slot.Id));
            classSlotUsed.Add((req.ClassId, slot.Id));
            teacherHours[req.TeacherId] = teacherHours.GetValueOrDefault(req.TeacherId) + 1;

            var dayKey = (req.ClassId, req.SubjectId, slot.Day);
            classSubjectDayCount[dayKey] = classSubjectDayCount.GetValueOrDefault(dayKey, 0) + 1;
        }
    }
}