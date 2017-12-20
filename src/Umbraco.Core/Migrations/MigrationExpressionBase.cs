﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NPoco;
using Umbraco.Core.Logging;
using Umbraco.Core.Persistence;
using Umbraco.Core.Persistence.SqlSyntax;

namespace Umbraco.Core.Migrations
{
    /// <summary>
    /// Provides a base class for migration expressions.
    /// </summary>
    public abstract class MigrationExpressionBase : IMigrationExpression
    {
        private bool _executed;
        private List<IMigrationExpression> _expressions;

        protected MigrationExpressionBase(IMigrationContext context, DatabaseType[] supportedDatabaseTypes = null)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            SupportedDatabaseTypes = supportedDatabaseTypes;
        }

        protected IMigrationContext Context { get; }

        protected ILogger Logger => Context.Logger;

        protected ISqlSyntaxProvider SqlSyntax => Context.Database.SqlContext.SqlSyntax;

        protected IUmbracoDatabase Database => Context.Database;

        public DatabaseType DatabaseType => Context.Database.DatabaseType;

        public List<IMigrationExpression> Expressions => _expressions ?? (_expressions = new List<IMigrationExpression>());

        public DatabaseType[] SupportedDatabaseTypes { get; }

        public bool IsExpressionSupported() // fixme - do we need this?!
        {
            return SupportedDatabaseTypes == null
                || SupportedDatabaseTypes.Length == 0
                // beware!
                // DatabaseType.SqlServer2005 = DatabaseTypes.SqlServerDatabaseType
                // DatabaseType.SqlServer2012 = DatabaseTypes.SqlServer2012DatabaseType
                // with cascading inheritance, so if SqlServer2005 is "supported" we
                // need to accept SqlServer2012 too => cannot simply test with "Contains"
                // and have to test the types.
                //|| SupportedDatabaseTypes.Contains(CurrentDatabaseType);
                || SupportedDatabaseTypes.Any(x => DatabaseType.GetType().Inherits(x.GetType()));
        }

        public virtual string Process(IMigrationContext context)
        {
            return ToString();
        }

        protected virtual string GetSql()
        {
            return ToString();
        }

        public void Execute()
        {
            if (_executed)
                throw new InvalidOperationException("This expression has already been executed.");
            _executed = true;

            var sql = GetSql();

            if (string.IsNullOrWhiteSpace(sql))
            {
                Logger.Info(GetType(), $"SQL [{Context.Index}]: <empty>");
                Context.Index++;
                return;
            }

            // split multiple statements - required for SQL CE
            // http://stackoverflow.com/questions/13665491/sql-ce-inconsistent-with-multiple-statements
            var stmtBuilder = new StringBuilder();
            using (var reader = new StringReader(sql))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.Equals("GO", StringComparison.OrdinalIgnoreCase))
                        ExecuteStatement(stmtBuilder);
                    else
                        stmtBuilder.AppendLine(line);
                }

                if (stmtBuilder.Length > 0)
                    ExecuteStatement(stmtBuilder);
            }

            Context.Index++;

            if (_expressions == null)
                return;

            foreach (var expression in _expressions)
                expression.Execute();
        }

        private void ExecuteStatement(StringBuilder stmtBuilder)
        {
            var stmt = stmtBuilder.ToString();
            Logger.Info(GetType(), $"SQL [{Context.Index}]: {stmt}");
            Database.Execute(stmt);
            stmtBuilder.Clear();
        }

        protected void AppendStatementSeparator(StringBuilder stmtBuilder)
        {
            stmtBuilder.AppendLine(";");
            if (DatabaseType.IsSqlServerOrCe())
                stmtBuilder.AppendLine("GO");
        }

        /// <summary>
        /// This might be useful in the future if we add it to the interface, but for now it's used to hack the DeleteAppTables & DeleteForeignKeyExpression
        /// to ensure they are not executed twice.
        /// </summary>
        internal string Name { get; set; }

        protected string GetQuotedValue(object val)
        {
            if (val == null) return "NULL";

            var type = val.GetType();

            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Boolean:
                    return ((bool)val) ? "1" : "0";
                case TypeCode.Single:
                case TypeCode.Double:
                case TypeCode.Decimal:
                case TypeCode.SByte:
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                case TypeCode.Byte:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                case TypeCode.UInt64:
                    return val.ToString();
                case TypeCode.DateTime:
                    return SqlSyntax.GetQuotedValue(SqlSyntax.FormatDateTime((DateTime) val));
                default:
                    return SqlSyntax.GetQuotedValue(val.ToString());
            }
        }
    }
}