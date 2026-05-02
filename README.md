# School Scheduler API

A .NET 10 REST API that automatically generates weekly school timetables using a **CSP backtracking algorithm with forward checking**. Built with ASP.NET Core, Dapper, PostgreSQL, and Scalar UI.

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
├── TimetableEngine.cs          # CSP backtracking scheduling algorithm
├── SchedulerController.cs      # API controller
├── CurriculumRepository.cs     # Database queries
└── Models.cs                   # TimeSlot, ClassRequirements, SchedulePlacement, PlacementTask
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
  "error": "Algorithm explored all possible combinations. These constraints are impossible to fulfill."
}
```

---

## How the Scheduling Algorithm Works

The algorithm (`TimetableEngine.cs`) uses **CSP Backtracking with Forward Checking** — a smarter approach that can undo bad decisions and try different combinations until a valid schedule is found.

### Key Classes

- `PlacementTask` — represents a single lesson to be scheduled. Contains the requirement (`ClassRequirements`) and a flag `IsDoublePeriod` indicating whether it needs two adjacent periods.

### Step 1 — Order Requirements
Hardest constraints are tackled first:
- Consecutive (double period) subjects go first
- Then subjects with the most weekly hours

### Step 2 — Flatten into Tasks
Each curriculum rule is broken into individual `PlacementTask` objects:
- A subject needing 5 hours with `RequiresConsecutive = true` → 2 double-period tasks + 1 single task (5 = 2+2+1)
- A subject needing 5 hours with `RequiresConsecutive = false` → 5 single tasks

For 12 classes × 30 hours = **360 individual tasks** to place.

### Step 3 — SolveCSP (Recursive Backtracking)

This is the core engine. It works like placing guests at a dinner table:

1. Pick the next unplaced task
2. Try every available slot (or adjacent slot pair for double periods)
3. **Forward Check** — before placing, verify the slot satisfies all constraints
4. If valid → place the lesson and recursively try to place the next task
5. If the next task fails → **backtrack**: undo the placement and try a different slot
6. If all slots are exhausted → return `false` to trigger the previous task to backtrack
7. If all tasks are placed → return `true` (success!)

### Step 4 — PlaceLesson / RemoveLesson

Every placement updates four tracking structures. RemoveLesson undoes all four — this is what makes backtracking possible:
- `teacherSlotUsed` — which slots a teacher is already in
- `classSlotUsed` — which slots a class already has
- `teacherHours` — running total of hours per teacher
- `classSubjectDayCount` — how many times a subject appears per class per day

### Constraints Checked Per Placement
- Teacher is not already teaching at that slot
- Class does not already have a lesson at that slot
- Teacher has not exceeded 25 hours/week
- Same subject not already scheduled that day (for non-consecutive subjects)
- Consecutive subjects must be placed as adjacent pairs on the same day

### Error Response
If the algorithm exhausts all possible combinations without finding a valid schedule:
```json
{
  "error": "Algorithm explored all possible combinations. These constraints are impossible to fulfill."
}
```
This means the database constraints (teacher assignments, hours) need to be fixed — not the code.

---

## Common Errors and Fixes

### "Algorithm explored all possible combinations. These constraints are impossible to fulfill."

The backtracking algorithm tried every possible slot combination and found no valid schedule. This always means the **data is the problem**, not the code. Common causes:

- A teacher is assigned too many hours (over 25/week)
- One teacher covers both Section A and Section B of the same grade and subject
- Not enough time slots exist to fit all required hours

**Diagnose teacher loads:**
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
- The algorithm uses backtracking — it will always find a valid schedule if one exists, or tell you it's impossible if not
- If the algorithm is too slow, the issue is almost always bad data (overloaded teachers) not the code itself

https://chatgpt.com/share/69f6038e-eedc-83eb-8632-6c60cf5cc2f7

https://gemini.google.com/share/eee8ec4d5ed5