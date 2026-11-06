namespace DbViz.Core.Model;

/// <summary>
/// Raw snapshot of the Oracle data dictionary for one schema. Mirrors the ALL_* views almost 1:1
/// so the extractor stays dumb and reliable, while all relationship logic lives in <see cref="SchemaIndex"/>.
/// </summary>
public sealed class Snapshot
{
    public const int CurrentFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;
    public string Schema { get; set; } = "";
    public DateTimeOffset ExtractedAt { get; set; }
    public string? DbVersion { get; set; }
    public string? ToolVersion { get; set; }

    public List<DbObjectRow> Objects { get; set; } = [];
    public List<TableRow> Tables { get; set; } = [];
    public List<ViewRow> Views { get; set; } = [];
    public List<MViewRow> MViews { get; set; } = [];
    public List<ColumnRow> Columns { get; set; } = [];
    public List<TableCommentRow> TableComments { get; set; } = [];
    public List<ColumnCommentRow> ColumnComments { get; set; } = [];
    public List<ConstraintRow> Constraints { get; set; } = [];
    public List<ConstraintColumnRow> ConstraintColumns { get; set; } = [];
    public List<IndexRow> Indexes { get; set; } = [];
    public List<IndexColumnRow> IndexColumns { get; set; } = [];
    public List<SubprogramRow> Subprograms { get; set; } = [];
    public List<ArgumentRow> Arguments { get; set; } = [];
    public List<SourceRow> Sources { get; set; } = [];
    public List<DependencyRow> Dependencies { get; set; } = [];
    public List<TriggerRow> Triggers { get; set; } = [];
    public List<SequenceRow> Sequences { get; set; } = [];
    public List<SynonymRow> Synonyms { get; set; } = [];
    public List<TypeRow> Types { get; set; } = [];
    public List<TypeAttributeRow> TypeAttributes { get; set; } = [];
    public List<CollectionTypeRow> CollectionTypes { get; set; } = [];
    public List<DbLinkRow> DbLinks { get; set; } = [];
    public List<JobRow> Jobs { get; set; } = [];
    public List<CompileErrorRow> Errors { get; set; } = [];

    /// <summary>Queries that failed (e.g. missing privileges); extraction continues without them.</summary>
    public List<ExtractWarning> Warnings { get; set; } = [];
}

public sealed record DbObjectRow(string Name, string Type, string Status, DateTime? Created, DateTime? LastDdlTime);

public sealed record TableRow(string Name, long? NumRows, DateTime? LastAnalyzed, bool Partitioned, bool Temporary, string? IotType);

public sealed record ViewRow(string Name, string? Text);

public sealed record MViewRow(string Name, string? Query, string? RefreshMode, string? RefreshMethod, DateTime? LastRefresh, string? Staleness);

public sealed record ColumnRow(
    string TableName, int ColumnId, string Name, string? DataType, string? DataTypeOwner,
    int? DataLength, int? DataPrecision, int? DataScale, int? CharLength, string? CharUsed,
    bool Nullable, string? DataDefault, bool Virtual, bool Identity);

public sealed record TableCommentRow(string TableName, string TableType, string Comment);

public sealed record ColumnCommentRow(string TableName, string ColumnName, string Comment);

/// <param name="Type">P, U, R, C</param>
public sealed record ConstraintRow(
    string Name, string Type, string TableName, string Status,
    string? RefOwner, string? RefConstraintName, string? RefTableName,
    string? DeleteRule, string? SearchCondition, bool GeneratedName);

public sealed record ConstraintColumnRow(string Owner, string ConstraintName, string TableName, string ColumnName, int? Position);

public sealed record IndexRow(string Name, string TableOwner, string TableName, string IndexType, bool Unique, string Status, bool Partitioned);

public sealed record IndexColumnRow(string IndexName, string ColumnName, int Position, bool Descending, string? Expression);

/// <param name="ObjectName">Package or standalone procedure/function.</param>
/// <param name="ProcedureName">Subprogram name inside a package; null for standalone units and for the package row itself.</param>
public sealed record SubprogramRow(string ObjectName, string? ProcedureName, string ObjectType, string? Overload, int SubprogramId,
    bool Pipelined, bool Deterministic, string? AuthId);

public sealed record ArgumentRow(
    string? PackageName, string ObjectName, string? Overload, int SubprogramId,
    int Position, int Sequence, string? ArgumentName, string? InOut,
    string? DataType, string? TypeOwner, string? TypeName, string? TypeSubname, string? PlsType,
    int? DataLength, int? DataPrecision, int? DataScale, bool Defaulted);

/// <param name="Type">PACKAGE, PACKAGE BODY, PROCEDURE, FUNCTION, TRIGGER, TYPE, TYPE BODY</param>
public sealed record SourceRow(string Name, string Type, string Text, int Lines);

public sealed record DependencyRow(string Name, string Type, string RefOwner, string RefName, string RefType, string? RefDbLink, string DependencyType);

public sealed record TriggerRow(string Name, string TriggerType, string TriggeringEvent, string? TableOwner, string? TableName,
    string BaseObjectType, string Status, string? WhenClause, string? Description);

public sealed record SequenceRow(string Name, string? MinValue, string? MaxValue, string? IncrementBy, bool Cycle, bool Order, string? CacheSize, string? LastNumber);

public sealed record SynonymRow(string Name, string? TableOwner, string? TableName, string? DbLink);

public sealed record TypeRow(string Name, string? TypeCode, int? Attributes, int? Methods, string? SuperTypeName);

public sealed record TypeAttributeRow(string TypeName, int AttrNo, string AttrName, string? AttrTypeOwner, string? AttrTypeName,
    int? Length, int? Precision, int? Scale);

public sealed record CollectionTypeRow(string TypeName, string CollType, string? ElemTypeOwner, string? ElemTypeName, string? UpperBound);

public sealed record DbLinkRow(string Name, string? Host, DateTime? Created);

public sealed record JobRow(string Name, string? JobType, string? JobAction, string? ProgramName, string? RepeatInterval,
    bool Enabled, string? State, string? Comments, string? LastStartDate);

public sealed record CompileErrorRow(string Name, string Type, int Line, int Position, string Text, string Attribute);

public sealed record ExtractWarning(string Query, string Message);
