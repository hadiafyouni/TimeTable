using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Dapper;
using Npgsql;

namespace Schedule
{
    public class CurriculumRepository
    {
        private readonly string _connectionString;

        public CurriculumRepository(string connectionString)
        {
            _connectionString = connectionString;
        }

        public List<TimeSlot> GetTimeSlots()
        {
            using (IDbConnection db = new NpgsqlConnection(_connectionString))
            {
                return db.Query<TimeSlot>("SELECT slot_id as Id, day_of_week as Day, period_number as Period FROM time_slots").ToList();
            }
        }

        public List<ClassRequirements> GetClassRequirements()
        {
            using (IDbConnection db = new NpgsqlConnection(_connectionString))
            {
                string sql = @"
            SELECT 
                c.class_id as ClassId,
                cr.subject_id as SubjectId,
                ta.teacher_id as TeacherId,
                cr.weekly_hours as RequiredHours,
                cr.requires_consecutive as RequiresConsecutive,
                c.section as Section
            FROM curriculum_rules cr
            JOIN classes c ON cr.grade_id = c.grade_id
            JOIN teacher_assignments ta ON ta.subject_id = cr.subject_id 
                AND ta.grade_id = cr.grade_id
            WHERE 
                (c.section = 'A' AND ta.teacher_id IN (
                    SELECT MIN(teacher_id) 
                    FROM teacher_assignments ta2 
                    WHERE ta2.subject_id = ta.subject_id 
                    AND ta2.grade_id = ta.grade_id
                ))
                OR
                (c.section = 'B' AND ta.teacher_id IN (
                    SELECT MAX(teacher_id) 
                    FROM teacher_assignments ta2 
                    WHERE ta2.subject_id = ta.subject_id 
                    AND ta2.grade_id = ta.grade_id
                ))";
                return db.Query<ClassRequirements>(sql).ToList();
            }
        }

        public void SaveScheduleToDatabase(List<SchedulePlacement> schedule, Guid version)
        {
            using (IDbConnection db = new NpgsqlConnection(_connectionString))
            {
                string sql = @"INSERT INTO timetables (schedule_version, class_id, slot_id, teacher_id, subject_id, is_active) 
                               VALUES (@Version, @ClassId, @SlotId, @TeacherId, @SubjectId, FALSE)";

                var parameters = schedule.Select(s => new { Version = version, s.ClassId, s.SlotId, s.TeacherId, s.SubjectId });
                db.Execute(sql, parameters);
            }
        }
    }
}