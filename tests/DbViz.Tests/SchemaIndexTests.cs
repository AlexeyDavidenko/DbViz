using DbViz.Core;
using DbViz.Core.Demo;
using DbViz.Core.Model;

namespace DbViz.Tests;

public class SchemaIndexTests
{
    private readonly SchemaIndex _ix = new(DemoSnapshot.Create());

    [Fact]
    public void Recycle_bin_objects_are_excluded()
    {
        Assert.DoesNotContain(_ix.Objects, o => o.Name.StartsWith("BIN$"));
    }

    [Fact]
    public void Exclude_masks_treat_underscore_literally()
    {
        Assert.True(_ix.IsExcludedName("SYS_C0012345"));
        Assert.False(_ix.IsExcludedName("SYSTEM_CONFIG"));
        Assert.False(_ix.IsExcludedName("SYS_USERS"));
        Assert.True(_ix.IsExcludedName("ISEQ$$_12345"));
    }

    [Fact]
    public void Package_spec_and_body_are_one_object()
    {
        Assert.Single(_ix.Objects, o => o.Name == "PKG_ORDERS");
        Assert.Equal(ObjectKind.Package, _ix.FindMain("PKG_ORDERS")!.Kind);
    }

    [Fact]
    public void Mview_container_table_is_not_a_separate_table()
    {
        var mv = Assert.Single(_ix.Objects, o => o.Name == "MV_DAILY_SALES");
        Assert.Equal(ObjectKind.MaterializedView, mv.Kind);
    }

    [Fact]
    public void Sys_and_self_dependencies_are_hidden()
    {
        var pkg = _ix.FindMain("PKG_ORDERS")!;
        Assert.DoesNotContain(_ix.Outgoing(pkg.Id), e => e.ToExternal?.Owner == "SYS");
        Assert.DoesNotContain(_ix.Outgoing(pkg.Id), e => e.ToId == pkg.Id);
        Assert.Contains(_ix.Outgoing(pkg.Id), e => e.ToId == "DEMO.ORDERS");
    }

    [Fact]
    public void Trigger_on_table_is_not_duplicated_as_dependency()
    {
        var edges = _ix.Incoming("DEMO.ORDERS").Where(e => e.FromId == "DEMO.TRG_ORDERS_AUDIT~TRG").ToList();
        var e = Assert.Single(edges);
        Assert.Equal(EdgeKind.TriggerOn, e.Kind);
        // while the trigger's write into AUDIT_LOG stays a regular dependency
        Assert.Contains(_ix.Incoming("DEMO.AUDIT_LOG"), x => x.FromId == "DEMO.TRG_ORDERS_AUDIT~TRG" && x.Kind == EdgeKind.Depends);
    }

    [Fact]
    public void Fk_to_other_schema_is_external()
    {
        var fk = Assert.Single(_ix.Outgoing("DEMO.PRODUCTS"), e => e.Kind == EdgeKind.Fk);
        Assert.Null(fk.ToId);
        Assert.Equal("SUPPLY.SUPPLIERS", fk.ToExternal!.Display);
    }

    [Fact]
    public void Dependency_through_own_db_link_points_to_link_note()
    {
        Assert.Contains(_ix.Outgoing("DEMO.RATES"), e => e.ToId == "DEMO.FINLINK~DBLINK" && e.Detail == "FIN.RATES");
    }

    [Fact]
    public void Private_db_link_missing_in_all_objects_still_becomes_object()
    {
        Assert.NotNull(_ix.Get("DEMO.FINLINK~DBLINK"));
    }

    [Fact]
    public void Self_referencing_fk_is_kept()
    {
        Assert.Contains(_ix.Outgoing("DEMO.CUSTOMERS"), e => e.Kind == EdgeKind.Fk && e.ToId == "DEMO.CUSTOMERS");
        Assert.Equal(DemoSnapshot.Create().Constraints.Count(c => c.Type == "R"), _ix.Edges.Count(e => e.Kind == EdgeKind.Fk));
    }

    [Fact]
    public void Opaque_types_are_formatted_by_type_name()
    {
        var arg = _ix.ArgumentsOf("PKG_ORDERS", "GET_TOTAL", null).Single(a => a.ArgumentName == "P_DOC");
        Assert.Equal("XMLTYPE", OracleTypes.Format(arg));
    }

    [Fact]
    public void Job_action_references_are_detected()
    {
        Assert.Contains(_ix.Outgoing("DEMO.JOB_RECALC_STOCK~JOB"), e => e.Kind == EdgeKind.JobCalls && e.ToId == "DEMO.RECALC_STOCK");
    }

    [Fact]
    public void Procedure_without_parameters_has_no_argument_rows()
    {
        Assert.Empty(_ix.ArgumentsOf(null, "RECALC_STOCK", null));
        Assert.True(SchemaIndex.IsFunction(_ix.ArgumentsOf("PKG_ORDERS", "GET_TOTAL", null)));
    }

    [Fact]
    public void Snapshot_json_roundtrip_preserves_data()
    {
        var s = DemoSnapshot.Create();
        var back = SnapshotSerializer.FromJson(SnapshotSerializer.ToJson(s));
        Assert.Equal(s.Objects.Count, back.Objects.Count);
        Assert.Equal(s.Sources.Single(x => x.Type == "PACKAGE BODY").Text, back.Sources.Single(x => x.Type == "PACKAGE BODY").Text);
        Assert.Equal(s.ColumnComments[0], back.ColumnComments[0]);
    }

    [Fact]
    public void Source_comments_are_extracted()
    {
        var spec = DemoSnapshot.Create().Sources.Single(x => x.Name == "PKG_ORDERS" && x.Type == "PACKAGE").Text;
        Assert.Equal("Order operations: create, cancel, compute the total.", SourceComments.HeaderComment(spec));
        Assert.Equal("Creates an order with its lines and returns its ID", SourceComments.CommentBefore(spec, "CREATE_ORDER"));
        Assert.Null(SourceComments.CommentBefore(spec, "GET_TOTAL"));
        Assert.Null(SourceComments.HeaderComment("PROCEDURE x IS\nBEGIN\n  NULL; -- not a header\nEND;"));
    }
}
