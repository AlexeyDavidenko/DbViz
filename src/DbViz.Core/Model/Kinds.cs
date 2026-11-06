namespace DbViz.Core.Model;

public enum ObjectKind
{
    Table,
    Sequence,
    View,
    MaterializedView,
    Synonym,
    Package,
    Procedure,
    Function,
    Trigger,
    Type,
    DbLink,
    Job,
}

public enum Layer
{
    Storage,      // L0
    Access,       // L1
    Logic,        // L2
    Integration,  // L3
}

public enum EdgeKind
{
    /// <summary>Foreign key table → table.</summary>
    Fk,
    /// <summary>ALL_DEPENDENCIES: kind of access is unknown.</summary>
    Depends,
    /// <summary>Trigger defined on a table/view.</summary>
    TriggerOn,
    /// <summary>Synonym points to an object.</summary>
    SynonymFor,
    /// <summary>Job calls an object (heuristic over the job_action text).</summary>
    JobCalls,
}

public static class KindInfo
{
    public static Layer LayerOf(ObjectKind kind) => kind switch
    {
        ObjectKind.Table or ObjectKind.Sequence => Layer.Storage,
        ObjectKind.View or ObjectKind.MaterializedView or ObjectKind.Synonym => Layer.Access,
        ObjectKind.Package or ObjectKind.Procedure or ObjectKind.Function or ObjectKind.Trigger => Layer.Logic,
        _ => Layer.Integration,
    };

    public static string LayerCode(Layer layer) => layer switch
    {
        Layer.Storage => "L0-storage",
        Layer.Access => "L1-access",
        Layer.Logic => "L2-logic",
        _ => "L3-integration",
    };

    public static string LayerTag(Layer layer) => layer switch
    {
        Layer.Storage => "layer/storage",
        Layer.Access => "layer/access",
        Layer.Logic => "layer/logic",
        _ => "layer/integration",
    };

    public static string TypeCode(ObjectKind kind) => kind switch
    {
        ObjectKind.MaterializedView => "mview",
        ObjectKind.DbLink => "dblink",
        _ => kind.ToString().ToLowerInvariant(),
    };

    public static string Title(ObjectKind kind) => kind switch
    {
        ObjectKind.Table => "Table",
        ObjectKind.Sequence => "Sequence",
        ObjectKind.View => "View",
        ObjectKind.MaterializedView => "Materialized view",
        ObjectKind.Synonym => "Synonym",
        ObjectKind.Package => "Package",
        ObjectKind.Procedure => "Procedure",
        ObjectKind.Function => "Function",
        ObjectKind.Trigger => "Trigger",
        ObjectKind.Type => "Type",
        ObjectKind.DbLink => "DB link",
        ObjectKind.Job => "Job",
        _ => kind.ToString(),
    };

    public static string Plural(ObjectKind kind) => kind switch
    {
        ObjectKind.Table => "Tables",
        ObjectKind.Sequence => "Sequences",
        ObjectKind.View => "Views",
        ObjectKind.MaterializedView => "Materialized views",
        ObjectKind.Synonym => "Synonyms",
        ObjectKind.Package => "Packages",
        ObjectKind.Procedure => "Procedures",
        ObjectKind.Function => "Functions",
        ObjectKind.Trigger => "Triggers",
        ObjectKind.Type => "Types",
        ObjectKind.DbLink => "DB links",
        ObjectKind.Job => "Jobs",
        _ => kind.ToString(),
    };

    public static string LayerTitle(Layer layer) => layer switch
    {
        Layer.Storage => "L0 Storage",
        Layer.Access => "L1 Access",
        Layer.Logic => "L2 Logic",
        _ => "L3 Types & integration",
    };
}
