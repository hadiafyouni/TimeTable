# School Scheduler API

A .NET 10 REST API that automatically generates weekly school timetables using a greedy scheduling algorithm. Built with ASP.NET Core, Dapper, PostgreSQL, and Scalar UI.

---

## Tech Stack

- .NET 10 / ASP.NET Core
- PostgreSQL
- Dapper (lightweight ORM)
- Npgsql (PostgreSQL driver)
- Scalar (API documentation UI)

---

## Project Structure

```
Schedule/
├── Program.cs                  # App entry point and middleware
├── Schedule.csproj             # Project dependencies
├── TimetableEngine.cs          # Scheduling algorithm
├── SchedulerController.cs      # API controller
├── CurriculumRepository.cs     # Database queries
└── Models.cs                   # TimeSlot, ClassRequirements, SchedulePlacement
```

---

## Database Setup

### Required Tables

```sql
-- Time slots (5 days x 6 periods = 30 slots)
SELECT * FROM time_slots;         -- columns: slot_id, day_of_week, period_number

-- Grades and sections (e.g. Grade 1A, 1B)
SELECT * FROM classes;            -- columns: class_id, grade_id, section

-- Subject definitions
SELECT * FROM subjects;           -- columns: subject_id, subject_name, department_id

-- Teachers
SELECT * FROM teachers;           -- columns: teacher_id, first_name, last_name, min_hours, max_hours

-- How many hours per subject per grade, and if consecutive periods are needed
SELECT * FROM curriculum_rules;   -- columns: rule_id, grade_id, subject_id, weekly_hours, requires_consecutive

-- Which teacher covers which subject for which grade
SELECT * FROM teacher_assignments; -- columns: assignment_id, subject_id, teacher_id, grade_id

-- Generated schedules
SELECT * FROM timetables;         -- columns: schedule_version, class_id, slot_id, teacher_id, subject_id, is_active
```

---

## Dependencies (.csproj)

```xml
<ItemGroup>
  <PackageReference Include="Dapper" Version="2.1.72" />
  <PackageReference Include="Microsoft.AspNetCore.OpenApi" Version="10.0.7" />
  <PackageReference Include="Npgsql" Version="10.0.2" />
  <PackageReference Include="Scalar.AspNetCore" Version="2.14.9" />
</ItemGroup>
```

---

## Configuration

Update the connection string in `Program.cs`:

```csharp
string connectionString =
    "Host=localhost;Database=SchoolScheduler;Username=postgres;Password=YourActualPassword;";
```

---

## Running the API

```bash
dotnet restore
dotnet build
dotnet run
```

Then open your browser and navigate to:

```
https://localhost:{port}/scalar/v1
```

This opens the Scalar UI where you can test the API.

---

## API Endpoints

### POST /api/scheduler/generate

Generates a full weekly timetable and saves it to the database.

**Request:** No body required.

**Success Response:**
```json
{
  "message": "Success",
  "version": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "data": [
    {
      "classId": 1,
      "slotId": 4,
      "teacherId": 1,
      "subjectId": 1
    }
  ]
}
```

**Error Response:**
```json
{
  "error": "Could not schedule subject 4 for class 4. Only placed 1/5 hours. Teacher 7 may be overloaded."
}
```

---

## How the Scheduling Algorithm Works

The algorithm (`TimetableEngine.cs`) uses a **greedy approach** to place lessons in one pass without backtracking.

### Step 1 — Flatten Requirements
Each curriculum rule (e.g. "English needs 5 hours for Grade 1") is expanded into individual lesson blocks. For 12 classes × 30 hours = **360 individual blocks** to place.

### Step 2 — Order Blocks
Hardest constraints are scheduled first:
- Consecutive (double period) subjects go first
- Then subjects with the most weekly hours

### Step 3 — Place Each Block
For each block the algorithm finds the first available slot that satisfies all constraints using HashSets for fast O(1) conflict checking.

### Constraints Checked Per Placement
- Teacher is not already teaching at that slot
- Class does not already have a lesson at that slot
- Teacher has not exceeded 25 hours/week
- Same subject not already scheduled that day (for non-consecutive subjects)
- Consecutive subjects must be placed in adjacent periods on the same day

---

## Common Errors and Fixes

### "Teacher capacities are exceeded"

A teacher is assigned too many hours across too many classes.

**Diagnose:**
```sql
SELECT 
    ta.teacher_id,
    t.last_name,
    SUM(cr.weekly_hours) as total_hours_required
FROM teacher_assignments ta
JOIN curriculum_rules cr ON cr.subject_id = ta.subject_id AND cr.grade_id = ta.grade_id
JOIN teachers t ON t.teacher_id = ta.teacher_id
GROUP BY ta.teacher_id, t.last_name
ORDER BY total_hours_required DESC;
```

**Rule:** Each teacher must have between 20 and 25 hours/week. Since each grade has 2 sections (A and B), one teacher cannot cover both sections of the same subject across all grades. You need **2 teachers per subject per set of grades**.

**Fix:** Split the load. For example, for English across grades 1-6:
- Teacher A → grades 1-3, section A
- Teacher B → grades 1-3, section B
- Teacher C → grades 4-6, section A
- Teacher D → grades 4-6, section B

### "Could not schedule subject X for class Y. Only placed Z/N hours."

The algorithm ran out of available slots for a teacher. This usually means:
- One teacher is assigned to both Section A and Section B of a grade
- Teacher has more than 25 required hours

**Check which classes a teacher covers:**
```sql
SELECT 
    ta.teacher_id,
    ta.grade_id,
    COUNT(c.class_id) as classes_in_grade,
    cr.weekly_hours
FROM teacher_assignments ta
JOIN classes c ON c.grade_id = ta.grade_id
JOIN curriculum_rules cr ON cr.subject_id = ta.subject_id AND cr.grade_id = ta.grade_id
WHERE ta.teacher_id = <teacher_id>
GROUP BY ta.teacher_id, ta.grade_id, cr.weekly_hours;
```

If `classes_in_grade` is 2, the teacher is covering both sections — assign a second teacher to one section.

---

## Teacher Assignment Rules

| Subject | Weekly Hours | Consecutive? | Teachers Needed (6 grades, 2 sections) |
|---------|-------------|--------------|----------------------------------------|
| English | 5 | No | 4 |
| Arabic | 5 | No | 4 |
| Math | 5 | Yes | 4 |
| Biology | 5 | No | 4 |
| History | 4 | No | 2 |
| Art | 3 | No | 2 |
| Sports | 3 | No | 2 |

Each teacher should have between **20 and 25 hours per week**.

---

## Checking Teacher Loads

```sql
SELECT 
    ta.teacher_id,
    t.last_name,
    SUM(cr.weekly_hours) as total_hours,
    COUNT(DISTINCT ta.grade_id) as num_grades
FROM teacher_assignments ta
JOIN curriculum_rules cr ON cr.subject_id = ta.subject_id AND cr.grade_id = ta.grade_id
JOIN teachers t ON t.teacher_id = ta.teacher_id
GROUP BY ta.teacher_id, t.last_name
ORDER BY total_hours DESC;
```

---

## Notes

- Each generated schedule is saved with a unique `schedule_version` UUID
- Schedules are saved with `is_active = FALSE` by default — you can add logic to activate a specific version
- The algorithm is deterministic per run but does not retry on failure — fix the data and re-run

https://chatgpt.com/share/69f6038e-eedc-83eb-8632-6c60cf5cc2f7

https://gemini.google.com/share/eee8ec4d5ed5