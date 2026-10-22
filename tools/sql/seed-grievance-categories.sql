-- NSG Mobile API - OPTIONAL seed of the grievance categories (Personal, Medical, Administration, Civil Dispute, Other).
--
-- Run by hand, once, ONLY after the migration 20261022103033_Add_Grievance_Management has been applied and reviewed, and first on a
-- throw-away or development database. The migration itself seeds nothing. Nothing in the application runs this script.
--
-- Safe to run twice: a category is inserted only if no category of that name exists (names are unique, case-insensitive, and an inactive
-- or deleted category still holds its name; to bring one back, set isActive with PUT /api/v1/admin/grievance-categories/{id}).
-- Categories carry no metadata: the upload limits are fixed in code. created_by is NULL for seeded rows (no admin acted).
--
-- created_at_ist is the current Indian Standard Time (UTC+05:30), independent of the server's and the session's time zone.

INSERT INTO `grievance_category` (`grievance_name`, `department_id`, `is_active`, `is_deleted`, `created_at_ist`, `created_by`)
SELECT v.`name`, NULL, 1, 0, CONVERT_TZ(UTC_TIMESTAMP(6), '+00:00', '+05:30'), NULL
FROM (SELECT 'Personal' AS `name` UNION ALL SELECT 'Medical' UNION ALL SELECT 'Administration'
      UNION ALL SELECT 'Civil Dispute' UNION ALL SELECT 'Other') AS v
WHERE NOT EXISTS (SELECT 1 FROM `grievance_category` c WHERE c.`grievance_name` = v.`name`);

-- Check:  SELECT `id`, `grievance_name`, `is_active`, `is_deleted` FROM `grievance_category` ORDER BY `grievance_name`;
