namespace DbViz.Oracle;

/// <summary>
/// ALL SQL the tool ever sends to the database. Dictionary SELECTs only, always filtered by owner (:owner).
/// No queries exist anywhere else in the code; each one goes through <see cref="ReadOnlyGuard"/>.
/// Targets Oracle 19c. Large numbers (sequences) are fetched via TO_CHAR to avoid decimal overflow.
/// </summary>
public static class DictionaryQueries
{
    public sealed record Query(string Name, string Sql);

    public static readonly Query CurrentUser = new("current_user",
        "SELECT USER AS username, SYS_CONTEXT('USERENV', 'DB_NAME') AS db_name FROM dual");

    public static readonly Query Version = new("version",
        "SELECT product || ' ' || version_full AS v FROM product_component_version WHERE product LIKE 'Oracle Database%'");

    public static readonly Query Objects = new("objects", """
        SELECT object_name, object_type, status, created, last_ddl_time
        FROM   all_objects
        WHERE  owner = :owner
          AND  object_type IN ('TABLE','VIEW','MATERIALIZED VIEW','SEQUENCE','SYNONYM','PACKAGE','PACKAGE BODY',
                                'PROCEDURE','FUNCTION','TRIGGER','TYPE','TYPE BODY','DATABASE LINK','JOB')
          AND  object_name NOT LIKE 'BIN$%'
        ORDER  BY object_name, object_type
        """);

    public static readonly Query ObjectCounts = new("object_counts", """
        SELECT object_type, status, COUNT(*) AS cnt
        FROM   all_objects
        WHERE  owner = :owner
        GROUP  BY object_type, status
        ORDER  BY object_type, status
        """);

    public static readonly Query Tables = new("tables", """
        SELECT table_name, num_rows, last_analyzed, partitioned, temporary, iot_type
        FROM   all_tables
        WHERE  owner = :owner AND nested = 'NO' AND secondary = 'N' AND dropped = 'NO'
        """);

    // TEXT is LONG; it is read in full thanks to InitialLONGFetchSize = -1
    public static readonly Query Views = new("views",
        "SELECT view_name, text FROM all_views WHERE owner = :owner");

    public static readonly Query MViews = new("mviews", """
        SELECT mview_name, query, refresh_mode, refresh_method, last_refresh_date, staleness
        FROM   all_mviews
        WHERE  owner = :owner
        """);

    public static readonly Query Columns = new("columns", """
        SELECT table_name, column_id, column_name, data_type, data_type_owner,
               data_length, data_precision, data_scale, char_length, char_used,
               nullable, data_default, virtual_column, identity_column
        FROM   all_tab_cols
        WHERE  owner = :owner AND hidden_column = 'NO' AND user_generated = 'YES'
          AND  table_name NOT LIKE 'BIN$%'
        """);

    public static readonly Query TableComments = new("table_comments", """
        SELECT table_name, table_type, comments
        FROM   all_tab_comments
        WHERE  owner = :owner AND comments IS NOT NULL
        """);

    public static readonly Query ColumnComments = new("column_comments", """
        SELECT table_name, column_name, comments
        FROM   all_col_comments
        WHERE  owner = :owner AND comments IS NOT NULL
        """);

    public static readonly Query Constraints = new("constraints", """
        SELECT c.constraint_name, c.constraint_type, c.table_name, c.status,
               c.r_owner, c.r_constraint_name, r.table_name AS r_table_name,
               c.delete_rule, c.search_condition, c.generated
        FROM   all_constraints c
        LEFT JOIN all_constraints r ON r.owner = c.r_owner AND r.constraint_name = c.r_constraint_name
        WHERE  c.owner = :owner AND c.constraint_type IN ('P','U','R','C')
          AND  c.table_name NOT LIKE 'BIN$%'
        """);

    // own columns + columns of other schemas' PK/UK referenced by our FKs
    public static readonly Query ConstraintColumns = new("constraint_columns", """
        SELECT cc.owner, cc.constraint_name, cc.table_name, cc.column_name, cc.position
        FROM   all_cons_columns cc
        WHERE  cc.owner = :owner AND cc.table_name NOT LIKE 'BIN$%'
        UNION ALL
        SELECT rc.owner, rc.constraint_name, rc.table_name, rc.column_name, rc.position
        FROM   all_constraints c
        JOIN   all_cons_columns rc ON rc.owner = c.r_owner AND rc.constraint_name = c.r_constraint_name
        WHERE  c.owner = :owner AND c.constraint_type = 'R' AND c.r_owner <> :owner
        """);

    public static readonly Query Indexes = new("indexes", """
        SELECT index_name, table_owner, table_name, index_type, uniqueness, status, partitioned
        FROM   all_indexes
        WHERE  owner = :owner AND index_type <> 'LOB' AND table_name NOT LIKE 'BIN$%'
        """);

    public static readonly Query IndexColumns = new("index_columns", """
        SELECT index_name, column_name, column_position, descend
        FROM   all_ind_columns
        WHERE  index_owner = :owner AND table_name NOT LIKE 'BIN$%'
        """);

    public static readonly Query IndexExpressions = new("index_expressions", """
        SELECT index_name, column_position, column_expression
        FROM   all_ind_expressions
        WHERE  index_owner = :owner
        """);

    public static readonly Query Subprograms = new("subprograms", """
        SELECT object_name, procedure_name, object_type, overload, subprogram_id, pipelined, deterministic, authid
        FROM   all_procedures
        WHERE  owner = :owner AND object_type IN ('PACKAGE','PROCEDURE','FUNCTION','TYPE')
        """);

    public static readonly Query Arguments = new("arguments", """
        SELECT package_name, object_name, overload, subprogram_id, position, sequence, argument_name, in_out,
               data_type, type_owner, type_name, type_subname, pls_type, data_length, data_precision, data_scale, defaulted
        FROM   all_arguments
        WHERE  owner = :owner AND data_level = 0
        """);

    public static readonly Query Source = new("source", """
        SELECT name, type, line, text
        FROM   all_source
        WHERE  owner = :owner
          AND  type IN ('PACKAGE','PACKAGE BODY','PROCEDURE','FUNCTION','TRIGGER','TYPE','TYPE BODY')
        ORDER  BY name, type, line
        """);

    public static readonly Query Dependencies = new("dependencies", """
        SELECT name, type, referenced_owner, referenced_name, referenced_type, referenced_link_name, dependency_type
        FROM   all_dependencies
        WHERE  owner = :owner
        """);

    public static readonly Query Triggers = new("triggers", """
        SELECT trigger_name, trigger_type, triggering_event, table_owner, table_name, base_object_type,
               status, when_clause, description
        FROM   all_triggers
        WHERE  owner = :owner
        """);

    public static readonly Query Sequences = new("sequences", """
        SELECT sequence_name, TO_CHAR(min_value) AS min_value, TO_CHAR(max_value) AS max_value,
               TO_CHAR(increment_by) AS increment_by, cycle_flag, order_flag,
               TO_CHAR(cache_size) AS cache_size, TO_CHAR(last_number) AS last_number
        FROM   all_sequences
        WHERE  sequence_owner = :owner
        """);

    public static readonly Query Synonyms = new("synonyms", """
        SELECT synonym_name, table_owner, table_name, db_link
        FROM   all_synonyms
        WHERE  owner = :owner
        """);

    public static readonly Query Types = new("types", """
        SELECT type_name, typecode, attributes, methods, supertype_name
        FROM   all_types
        WHERE  owner = :owner
        """);

    public static readonly Query TypeAttributes = new("type_attributes", """
        SELECT type_name, attr_no, attr_name, attr_type_owner, attr_type_name, length, precision, scale
        FROM   all_type_attrs
        WHERE  owner = :owner
        """);

    public static readonly Query CollectionTypes = new("collection_types", """
        SELECT type_name, coll_type, elem_type_owner, elem_type_name, TO_CHAR(upper_bound) AS upper_bound
        FROM   all_coll_types
        WHERE  owner = :owner
        """);

    public static readonly Query DbLinks = new("db_links", """
        SELECT db_link, host, created
        FROM   all_db_links
        WHERE  owner = :owner
        """);

    public static readonly Query Jobs = new("jobs", """
        SELECT job_name, job_type, job_action, program_name, repeat_interval, enabled, state, comments,
               TO_CHAR(last_start_date, 'YYYY-MM-DD HH24:MI:SS TZH:TZM') AS last_start_date
        FROM   all_scheduler_jobs
        WHERE  owner = :owner
        """);

    public static readonly Query Errors = new("errors", """
        SELECT name, type, line, position, text, attribute
        FROM   all_errors
        WHERE  owner = :owner
        ORDER  BY name, type, sequence
        """);

    public static IReadOnlyList<Query> All { get; } =
    [
        CurrentUser, Version, Objects, ObjectCounts, Tables, Views, MViews, Columns, TableComments, ColumnComments,
        Constraints, ConstraintColumns, Indexes, IndexColumns, IndexExpressions, Subprograms, Arguments, Source,
        Dependencies, Triggers, Sequences, Synonyms, Types, TypeAttributes, CollectionTypes, DbLinks, Jobs, Errors,
    ];
}
