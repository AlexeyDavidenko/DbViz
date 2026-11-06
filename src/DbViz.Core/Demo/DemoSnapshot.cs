using DbViz.Core.Model;

namespace DbViz.Core.Demo;

/// <summary>
/// Entirely fictional DEMO schema: tests and vault previews run on it without access to a real database.
/// Covers FK, PK/UK/CHECK, indexes, a view, an mview, a package with overloads, a standalone procedure/function (INVALID),
/// a trigger, a sequence, a synonym to another schema, collection types, a db link, a job and external dependencies.
/// </summary>
public static class DemoSnapshot
{
    public static Snapshot Create()
    {
        var d = new DateTime(2026, 10, 1, 12, 0, 0);
        var s = new Snapshot { Schema = "DEMO", ExtractedAt = new DateTimeOffset(2026, 11, 6, 9, 0, 0, TimeSpan.FromHours(3)), DbVersion = "Oracle Database 19c Demo 19.0.0.0.0" };

        void Obj(string name, string type, string status = "VALID") => s.Objects.Add(new DbObjectRow(name, type, status, d.AddDays(-100), d));
        foreach (var t in new[] { "CUSTOMERS", "ORDERS", "ORDER_ITEMS", "PRODUCTS", "AUDIT_LOG" }) Obj(t, "TABLE");
        Obj("V_ORDER_TOTALS", "VIEW");
        Obj("MV_DAILY_SALES", "MATERIALIZED VIEW"); Obj("MV_DAILY_SALES", "TABLE");
        Obj("PKG_ORDERS", "PACKAGE"); Obj("PKG_ORDERS", "PACKAGE BODY");
        Obj("RECALC_STOCK", "PROCEDURE");
        Obj("FN_FORMAT_NAME", "FUNCTION", "INVALID");
        Obj("TRG_ORDERS_AUDIT", "TRIGGER");
        Obj("SEQ_ORDERS", "SEQUENCE");
        Obj("RATES", "SYNONYM");
        Obj("T_ORDER_LINE", "TYPE"); Obj("T_ORDER_LINES", "TYPE");
        // FINLINK is intentionally NOT in Objects: private db links are not visible in ALL_OBJECTS (same as on real databases)
        Obj("JOB_RECALC_STOCK", "JOB");
        Obj("BIN$abc==$0", "TABLE"); // recycle bin: must be filtered out

        s.Tables.AddRange([
            new("CUSTOMERS", 1200, d, false, false, null),
            new("ORDERS", 50_000, d, true, false, null),
            new("ORDER_ITEMS", 210_000, d, false, false, null),
            new("PRODUCTS", 800, d, false, false, null),
            new("AUDIT_LOG", 1_000_000, d, false, false, null),
            new("MV_DAILY_SALES", 365, d, false, false, null),
        ]);

        void Col(string t, int id, string n, string type, bool nullable = true, int? len = null, int? p = null, int? sc = null, string? def = null, string? owner = null) =>
            s.Columns.Add(new ColumnRow(t, id, n, type, owner, len, p, sc, len, type.Contains("CHAR") ? "C" : null, nullable, def, false, false));

        Col("CUSTOMERS", 1, "CUSTOMER_ID", "NUMBER", false, p: 10, sc: 0);
        Col("CUSTOMERS", 2, "FULL_NAME", "VARCHAR2", false, 200);
        Col("CUSTOMERS", 3, "EMAIL", "VARCHAR2", true, 320);
        Col("CUSTOMERS", 4, "CREATED_AT", "DATE", false, def: "SYSDATE");
        Col("CUSTOMERS", 5, "REFERRER_ID", "NUMBER", true, p: 10, sc: 0);
        Col("ORDERS", 1, "ORDER_ID", "NUMBER", false, p: 12, sc: 0);
        Col("ORDERS", 2, "CUSTOMER_ID", "NUMBER", false, p: 10, sc: 0);
        Col("ORDERS", 3, "STATUS", "VARCHAR2", false, 20, def: "'NEW'");
        Col("ORDERS", 4, "TOTAL", "NUMBER", true, p: 14, sc: 2);
        Col("ORDERS", 5, "CREATED_AT", "TIMESTAMP(6)", false);
        Col("ORDER_ITEMS", 1, "ORDER_ID", "NUMBER", false, p: 12, sc: 0);
        Col("ORDER_ITEMS", 2, "LINE_NO", "NUMBER", false, p: 4, sc: 0);
        Col("ORDER_ITEMS", 3, "PRODUCT_ID", "NUMBER", false, p: 10, sc: 0);
        Col("ORDER_ITEMS", 4, "QTY", "NUMBER", false, p: 8, sc: 0);
        Col("ORDER_ITEMS", 5, "PRICE", "NUMBER", false, p: 12, sc: 2);
        Col("PRODUCTS", 1, "PRODUCT_ID", "NUMBER", false, p: 10, sc: 0);
        Col("PRODUCTS", 2, "SKU", "VARCHAR2", false, 40);
        Col("PRODUCTS", 3, "STOCK", "NUMBER", true);
        Col("AUDIT_LOG", 1, "EVENT_TIME", "TIMESTAMP(6)", false);
        Col("AUDIT_LOG", 2, "TABLE_NAME", "VARCHAR2", false, 128);
        Col("AUDIT_LOG", 3, "PAYLOAD", "CLOB", true);
        Col("V_ORDER_TOTALS", 1, "ORDER_ID", "NUMBER", false, p: 12, sc: 0);
        Col("V_ORDER_TOTALS", 2, "CUSTOMER_NAME", "VARCHAR2", false, 200);
        Col("V_ORDER_TOTALS", 3, "ITEMS_TOTAL", "NUMBER", true);
        Col("MV_DAILY_SALES", 1, "SALE_DATE", "DATE", false);
        Col("MV_DAILY_SALES", 2, "AMOUNT", "NUMBER", true);

        s.TableComments.AddRange([
            new("CUSTOMERS", "TABLE", "Customers (fictional data)"),
            new("ORDERS", "TABLE", "Customer orders"),
            new("ORDER_ITEMS", "TABLE", "Order lines"),
            new("V_ORDER_TOTALS", "VIEW", "Order total computed from its lines"),
        ]);
        s.ColumnComments.AddRange([
            new("ORDERS", "STATUS", "Status: NEW | PAID | CANCELLED"),
            new("ORDERS", "TOTAL", "Total | after discounts"), // "|" must be escaped inside a table
            new("CUSTOMERS", "EMAIL", "E-mail address"),
        ]);

        s.Constraints.AddRange([
            new("PK_CUSTOMERS", "P", "CUSTOMERS", "ENABLED", null, null, null, null, null, false),
            new("UK_CUSTOMERS_EMAIL", "U", "CUSTOMERS", "ENABLED", null, null, null, null, null, false),
            new("PK_ORDERS", "P", "ORDERS", "ENABLED", null, null, null, null, null, false),
            new("FK_ORDERS_CUSTOMER", "R", "ORDERS", "ENABLED", "DEMO", "PK_CUSTOMERS", "CUSTOMERS", "NO ACTION", null, false),
            new("CK_ORDERS_STATUS", "C", "ORDERS", "ENABLED", null, null, null, null, "STATUS IN ('NEW','PAID','CANCELLED')", false),
            new("SYS_C0012345", "C", "ORDERS", "ENABLED", null, null, null, null, "\"ORDER_ID\" IS NOT NULL", true),
            new("PK_ORDER_ITEMS", "P", "ORDER_ITEMS", "ENABLED", null, null, null, null, null, false),
            new("FK_ITEMS_ORDER", "R", "ORDER_ITEMS", "ENABLED", "DEMO", "PK_ORDERS", "ORDERS", "CASCADE", null, false),
            new("FK_ITEMS_PRODUCT", "R", "ORDER_ITEMS", "ENABLED", "DEMO", "PK_PRODUCTS", "PRODUCTS", "NO ACTION", null, false),
            new("PK_PRODUCTS", "P", "PRODUCTS", "ENABLED", null, null, null, null, null, false),
            new("FK_CUSTOMERS_REFERRER", "R", "CUSTOMERS", "ENABLED", "DEMO", "PK_CUSTOMERS", "CUSTOMERS", "SET NULL", null, false),
            new("FK_PRODUCTS_SUPPLIER", "R", "PRODUCTS", "DISABLED", "SUPPLY", "PK_SUPPLIERS", "SUPPLIERS", "NO ACTION", null, false),
        ]);
        void CC(string owner, string c, string t, string col, int pos) => s.ConstraintColumns.Add(new ConstraintColumnRow(owner, c, t, col, pos));
        CC("DEMO", "PK_CUSTOMERS", "CUSTOMERS", "CUSTOMER_ID", 1);
        CC("DEMO", "UK_CUSTOMERS_EMAIL", "CUSTOMERS", "EMAIL", 1);
        CC("DEMO", "FK_CUSTOMERS_REFERRER", "CUSTOMERS", "REFERRER_ID", 1);
        CC("DEMO", "PK_ORDERS", "ORDERS", "ORDER_ID", 1);
        CC("DEMO", "FK_ORDERS_CUSTOMER", "ORDERS", "CUSTOMER_ID", 1);
        CC("DEMO", "PK_ORDER_ITEMS", "ORDER_ITEMS", "ORDER_ID", 1);
        CC("DEMO", "PK_ORDER_ITEMS", "ORDER_ITEMS", "LINE_NO", 2);
        CC("DEMO", "FK_ITEMS_ORDER", "ORDER_ITEMS", "ORDER_ID", 1);
        CC("DEMO", "FK_ITEMS_PRODUCT", "ORDER_ITEMS", "PRODUCT_ID", 1);
        CC("DEMO", "PK_PRODUCTS", "PRODUCTS", "PRODUCT_ID", 1);
        CC("DEMO", "FK_PRODUCTS_SUPPLIER", "PRODUCTS", "SUPPLIER_ID", 1);
        CC("SUPPLY", "PK_SUPPLIERS", "SUPPLIERS", "SUPPLIER_ID", 1);

        s.Indexes.AddRange([
            new("PK_CUSTOMERS", "DEMO", "CUSTOMERS", "NORMAL", true, "VALID", false),
            new("UK_CUSTOMERS_EMAIL", "DEMO", "CUSTOMERS", "NORMAL", true, "VALID", false),
            new("PK_ORDERS", "DEMO", "ORDERS", "NORMAL", true, "VALID", false),
            new("IX_ORDERS_LOWER_STATUS", "DEMO", "ORDERS", "FUNCTION-BASED NORMAL", false, "VALID", false),
            new("PK_ORDER_ITEMS", "DEMO", "ORDER_ITEMS", "NORMAL", true, "VALID", false),
            new("PK_PRODUCTS", "DEMO", "PRODUCTS", "NORMAL", true, "VALID", false),
        ]);
        s.IndexColumns.AddRange([
            new("PK_CUSTOMERS", "CUSTOMER_ID", 1, false, null),
            new("UK_CUSTOMERS_EMAIL", "EMAIL", 1, false, null),
            new("PK_ORDERS", "ORDER_ID", 1, false, null),
            new("IX_ORDERS_LOWER_STATUS", "SYS_NC00006$", 1, false, "LOWER(\"STATUS\")"),
            new("PK_ORDER_ITEMS", "ORDER_ID", 1, false, null),
            new("PK_ORDER_ITEMS", "LINE_NO", 2, false, null),
            new("PK_PRODUCTS", "PRODUCT_ID", 1, false, null),
        ]);
        // FK_ORDERS_CUSTOMER and FK_ITEMS_PRODUCT have no index (they show up in the report)

        s.Views.Add(new ViewRow("V_ORDER_TOTALS", """
            SELECT o.order_id, c.full_name AS customer_name, SUM(i.qty * i.price) AS items_total
            FROM orders o JOIN customers c ON c.customer_id = o.customer_id
            JOIN order_items i ON i.order_id = o.order_id
            GROUP BY o.order_id, c.full_name
            """));
        s.MViews.Add(new MViewRow("MV_DAILY_SALES", "SELECT TRUNC(created_at) sale_date, SUM(total) amount FROM orders GROUP BY TRUNC(created_at)",
            "DEMAND", "COMPLETE", d, "FRESH"));

        s.Subprograms.AddRange([
            new("PKG_ORDERS", null, "PACKAGE", null, 0, false, false, "DEFINER"),
            new("PKG_ORDERS", "CREATE_ORDER", "PACKAGE", null, 1, false, false, "DEFINER"),
            new("PKG_ORDERS", "CANCEL_ORDER", "PACKAGE", "1", 2, false, false, "DEFINER"),
            new("PKG_ORDERS", "CANCEL_ORDER", "PACKAGE", "2", 3, false, false, "DEFINER"),
            new("PKG_ORDERS", "GET_TOTAL", "PACKAGE", null, 4, false, true, "DEFINER"),
            new("RECALC_STOCK", null, "PROCEDURE", null, 1, false, false, "DEFINER"),
            new("FN_FORMAT_NAME", null, "FUNCTION", null, 1, false, true, "CURRENT_USER"),
        ]);
        void Arg(string? pkg, string obj, string? ov, int pos, string? name, string? inout, string? type, string? pls = null,
            string? tOwner = null, string? tName = null, string? tSub = null, bool def = false) =>
            s.Arguments.Add(new ArgumentRow(pkg, obj, ov, 0, pos, pos + 1, name, inout, type, tOwner, tName, tSub, pls ?? type, null, null, null, def));
        Arg("PKG_ORDERS", "CREATE_ORDER", null, 1, "P_CUSTOMER_ID", "IN", "NUMBER");
        Arg("PKG_ORDERS", "CREATE_ORDER", null, 2, "P_LINES", "IN", "TABLE", null, "DEMO", "T_ORDER_LINES");
        Arg("PKG_ORDERS", "CREATE_ORDER", null, 3, "P_ORDER_ID", "OUT", "NUMBER");
        Arg("PKG_ORDERS", "CANCEL_ORDER", "1", 1, "P_ORDER_ID", "IN", "NUMBER");
        Arg("PKG_ORDERS", "CANCEL_ORDER", "2", 1, "P_ORDER_ID", "IN", "NUMBER");
        Arg("PKG_ORDERS", "CANCEL_ORDER", "2", 2, "P_REASON", "IN", "VARCHAR2", def: true);
        Arg("PKG_ORDERS", "GET_TOTAL", null, 0, null, "OUT", "NUMBER");
        Arg("PKG_ORDERS", "GET_TOTAL", null, 1, "P_ORDER_ID", "IN", "NUMBER");
        Arg(null, "RECALC_STOCK", null, 1, null, "IN", null); // procedure without parameters
        Arg(null, "FN_FORMAT_NAME", null, 0, null, "OUT", "VARCHAR2");
        Arg(null, "FN_FORMAT_NAME", null, 1, "P_NAME", "IN", "VARCHAR2");
        Arg("PKG_ORDERS", "GET_TOTAL", null, 2, "P_DOC", "IN", "OPAQUE/XMLTYPE", pls: "OPAQUE/XMLTYPE", tOwner: "SYS", tName: "XMLTYPE", def: true);

        s.Sources.AddRange([
            new("PKG_ORDERS", "PACKAGE", """
                PACKAGE pkg_orders AS
                  /* Order operations: create, cancel, compute the total. */

                  -- Creates an order with its lines and returns its ID
                  PROCEDURE create_order(p_customer_id IN NUMBER, p_lines IN t_order_lines, p_order_id OUT NUMBER);
                  -- Cancels an order
                  PROCEDURE cancel_order(p_order_id IN NUMBER);
                  PROCEDURE cancel_order(p_order_id IN NUMBER, p_reason IN VARCHAR2 DEFAULT NULL);
                  FUNCTION get_total(p_order_id IN NUMBER) RETURN NUMBER DETERMINISTIC;
                END pkg_orders;
                """, 10),
            new("PKG_ORDERS", "PACKAGE BODY", """
                PACKAGE BODY pkg_orders AS
                  PROCEDURE create_order(p_customer_id IN NUMBER, p_lines IN t_order_lines, p_order_id OUT NUMBER) IS
                  BEGIN
                    p_order_id := seq_orders.NEXTVAL;
                    INSERT INTO orders(order_id, customer_id) VALUES (p_order_id, p_customer_id);
                  END;
                  PROCEDURE cancel_order(p_order_id IN NUMBER) IS BEGIN cancel_order(p_order_id, NULL); END;
                  PROCEDURE cancel_order(p_order_id IN NUMBER, p_reason IN VARCHAR2 DEFAULT NULL) IS
                  BEGIN UPDATE orders SET status = 'CANCELLED' WHERE order_id = p_order_id; END;
                  FUNCTION get_total(p_order_id IN NUMBER) RETURN NUMBER DETERMINISTIC IS
                    v NUMBER;
                  BEGIN SELECT items_total INTO v FROM v_order_totals WHERE order_id = p_order_id; RETURN v; END;
                END pkg_orders;
                """, 14),
            new("RECALC_STOCK", "PROCEDURE", """
                PROCEDURE recalc_stock IS
                -- Recalculates product stock from order lines
                BEGIN
                  UPDATE products p SET stock = stock - (SELECT NVL(SUM(qty),0) FROM order_items i WHERE i.product_id = p.product_id);
                END;
                """, 5),
            new("FN_FORMAT_NAME", "FUNCTION", """
                FUNCTION fn_format_name(p_name IN VARCHAR2) RETURN VARCHAR2 IS
                BEGIN
                  RETURN hr.employees_api.format(p_name);
                END;
                """, 4),
            new("TRG_ORDERS_AUDIT", "TRIGGER", """
                TRIGGER trg_orders_audit AFTER INSERT OR UPDATE ON orders FOR EACH ROW
                BEGIN
                  INSERT INTO audit_log(event_time, table_name) VALUES (SYSTIMESTAMP, 'ORDERS');
                END;
                """, 4),
            new("T_ORDER_LINE", "TYPE", "TYPE t_order_line AS OBJECT (product_id NUMBER(10), qty NUMBER(8));", 1),
            new("T_ORDER_LINES", "TYPE", "TYPE t_order_lines AS TABLE OF t_order_line;", 1),
        ]);

        void Dep(string n, string t, string ro, string rn, string rt, string? link = null) =>
            s.Dependencies.Add(new DependencyRow(n, t, ro, rn, rt, link, "HARD"));
        Dep("V_ORDER_TOTALS", "VIEW", "DEMO", "ORDERS", "TABLE");
        Dep("V_ORDER_TOTALS", "VIEW", "DEMO", "CUSTOMERS", "TABLE");
        Dep("V_ORDER_TOTALS", "VIEW", "DEMO", "ORDER_ITEMS", "TABLE");
        Dep("MV_DAILY_SALES", "MATERIALIZED VIEW", "DEMO", "ORDERS", "TABLE");
        Dep("PKG_ORDERS", "PACKAGE", "DEMO", "T_ORDER_LINES", "TYPE");
        Dep("PKG_ORDERS", "PACKAGE", "SYS", "STANDARD", "PACKAGE"); // noise: must be hidden
        Dep("PKG_ORDERS", "PACKAGE BODY", "DEMO", "PKG_ORDERS", "PACKAGE"); // self-reference: hidden
        Dep("PKG_ORDERS", "PACKAGE BODY", "DEMO", "ORDERS", "TABLE");
        Dep("PKG_ORDERS", "PACKAGE BODY", "DEMO", "SEQ_ORDERS", "SEQUENCE");
        Dep("PKG_ORDERS", "PACKAGE BODY", "DEMO", "V_ORDER_TOTALS", "VIEW");
        Dep("RECALC_STOCK", "PROCEDURE", "DEMO", "PRODUCTS", "TABLE");
        Dep("RECALC_STOCK", "PROCEDURE", "DEMO", "ORDER_ITEMS", "TABLE");
        Dep("FN_FORMAT_NAME", "FUNCTION", "HR", "EMPLOYEES_API", "PACKAGE");
        Dep("TRG_ORDERS_AUDIT", "TRIGGER", "DEMO", "ORDERS", "TABLE");
        Dep("TRG_ORDERS_AUDIT", "TRIGGER", "DEMO", "AUDIT_LOG", "TABLE");
        Dep("RATES", "SYNONYM", "FIN", "RATES", "TABLE", "FINLINK");
        Dep("T_ORDER_LINES", "TYPE", "DEMO", "T_ORDER_LINE", "TYPE");

        s.Triggers.Add(new TriggerRow("TRG_ORDERS_AUDIT", "AFTER EACH ROW", "INSERT OR UPDATE", "DEMO", "ORDERS", "TABLE", "ENABLED", null, "trg_orders_audit AFTER INSERT OR UPDATE ON orders"));
        s.Sequences.Add(new SequenceRow("SEQ_ORDERS", "1", "9999999999999999999999999999", "1", false, false, "20", "50021"));
        s.Synonyms.Add(new SynonymRow("RATES", "FIN", "RATES", "FINLINK"));
        s.Types.AddRange([new("T_ORDER_LINE", "OBJECT", 2, 0, null), new("T_ORDER_LINES", "COLLECTION", 0, 0, null)]);
        s.TypeAttributes.AddRange([
            new("T_ORDER_LINE", 1, "PRODUCT_ID", null, "NUMBER", null, 10, 0),
            new("T_ORDER_LINE", 2, "QTY", null, "NUMBER", null, 8, 0),
        ]);
        s.CollectionTypes.Add(new("T_ORDER_LINES", "TABLE", "DEMO", "T_ORDER_LINE", null));
        s.DbLinks.Add(new DbLinkRow("FINLINK", "fin-db.example.local:1521/FIN", d));
        s.Jobs.Add(new JobRow("JOB_RECALC_STOCK", "PLSQL_BLOCK", "BEGIN demo.recalc_stock; END;", null, "FREQ=DAILY;BYHOUR=2", true, "SCHEDULED", "Nightly stock recalculation", "2026-11-05 02:00:00 +03:00"));
        s.Errors.Add(new CompileErrorRow("FN_FORMAT_NAME", "FUNCTION", 3, 10, "PLS-00201: identifier 'HR.EMPLOYEES_API' must be declared", "ERROR"));
        s.Warnings.Add(new ExtractWarning("jobs_demo", "Sample warning: ORA-00942 table or view does not exist"));
        return s;
    }
}
