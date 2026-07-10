Imports System.Reflection
Imports System.Text
Imports Npgsql
Imports NpgsqlTypes

Namespace Data

    ''' <summary>
    ''' A single relational table backed by PostgreSQL, exposing the same API as
    ''' the old JsonTable(Of T) so the rest of the app is unchanged. Each public
    ''' property of T maps to a real, typed column (no JSON blobs). Reads load the
    ''' rows and run the predicate in memory; writes replace the whole table's
    ''' contents inside a transaction — correct for any add/remove/edit and
    ''' atomic, at GDPS scale (small tables).
    ''' </summary>
    Public Class PgTable(Of T As New)

        Private ReadOnly _ds As NpgsqlDataSource
        Private ReadOnly _table As String
        Private ReadOnly _pk As String
        Private ReadOnly _props As PropertyInfo()
        Private ReadOnly _cols As String()
        Private ReadOnly _gate As New Object()

        Public Sub New(ds As NpgsqlDataSource, tableName As String)
            _ds = ds
            _table = tableName
            _props = GetType(T).GetProperties(BindingFlags.Public Or BindingFlags.Instance).
                        Where(Function(p) p.CanRead AndAlso p.CanWrite).ToArray()
            _cols = _props.Select(Function(p) ColName(p.Name)).ToArray()
            _pk = _cols(0) ' first property is the primary key for every model
            EnsureTable()
        End Sub

        ' --- schema ---------------------------------------------------------

        Private Shared Function ColName(propName As String) As String
            ' PascalCase -> snake_case, keeping acronyms intact:
            ' ID -> id, AccountID -> account_id, PetusId -> petus_id, GJP2 -> gjp2.
            Dim sb As New StringBuilder()
            For i = 0 To propName.Length - 1
                Dim c = propName(i)
                If i > 0 AndAlso Char.IsUpper(c) Then
                    Dim prev = propName(i - 1)
                    Dim nextIsLower = i + 1 < propName.Length AndAlso Char.IsLower(propName(i + 1))
                    ' underscore at a lower->Upper boundary, or Upper->Upper->lower (acronym end)
                    If Char.IsLower(prev) OrElse Char.IsDigit(prev) OrElse (Char.IsUpper(prev) AndAlso nextIsLower) Then
                        sb.Append("_"c)
                    End If
                End If
                sb.Append(Char.ToLowerInvariant(c))
            Next
            Return sb.ToString()
        End Function

        Private Shared Function SqlType(t As Type) As String
            t = If(Nullable.GetUnderlyingType(t), t)
            Select Case Type.GetTypeCode(t)
                Case TypeCode.Int32 : Return "integer"
                Case TypeCode.Int64 : Return "bigint"
                Case TypeCode.Boolean : Return "boolean"
                Case TypeCode.Double, TypeCode.Single : Return "double precision"
                Case Else : Return "text"
            End Select
        End Function

        Private Sub EnsureTable()
            Dim defs As New List(Of String)()
            For i = 0 To _props.Length - 1
                Dim col = _cols(i)
                Dim sqlT = SqlType(_props(i).PropertyType)
                Dim def = $"""{col}"" {sqlT}"
                If i = 0 Then def &= " PRIMARY KEY"
                defs.Add(def)
            Next
            Dim ddl = $"CREATE TABLE IF NOT EXISTS ""{_table}"" ({String.Join(", ", defs)})"
            Using cmd = _ds.CreateCommand(ddl)
                cmd.ExecuteNonQuery()
            End Using
            ' Add any columns missing on an older table (lightweight migration).
            For i = 0 To _props.Length - 1
                Dim alter = $"ALTER TABLE ""{_table}"" ADD COLUMN IF NOT EXISTS ""{_cols(i)}"" {SqlType(_props(i).PropertyType)}"
                Using cmd = _ds.CreateCommand(alter)
                    cmd.ExecuteNonQuery()
                End Using
            Next
        End Sub

        ' --- mapping --------------------------------------------------------

        Private Function ReadRow(r As NpgsqlDataReader) As T
            Dim obj As New T()
            For i = 0 To _props.Length - 1
                If r.IsDBNull(i) Then Continue For
                Dim val = r.GetValue(i)
                Dim pt = _props(i).PropertyType
                Dim target = If(Nullable.GetUnderlyingType(pt), pt)
                Try
                    _props(i).SetValue(obj, Convert.ChangeType(val, target))
                Catch
                    _props(i).SetValue(obj, val)
                End Try
            Next
            Return obj
        End Function

        Private Function LoadAll() As List(Of T)
            Dim list As New List(Of T)()
            Using cmd = _ds.CreateCommand($"SELECT {String.Join(", ", _cols.Select(Function(c) $"""{c}"""))} FROM ""{_table}""")
                Using r = cmd.ExecuteReader()
                    While r.Read()
                        list.Add(ReadRow(r))
                    End While
                End Using
            End Using
            Return list
        End Function

        ' --- public API (mirrors JsonTable) --------------------------------

        Public Function Read(Of R)(reader As Func(Of List(Of T), R)) As R
            SyncLock _gate
                Return reader(LoadAll())
            End SyncLock
        End Function

        Public Sub Write(mutator As Action(Of List(Of T)))
            SyncLock _gate
                Dim rows = LoadAll()
                mutator(rows)
                Persist(rows)
            End SyncLock
        End Sub

        Public Function WriteReturn(Of R)(mutator As Func(Of List(Of T), R)) As R
            SyncLock _gate
                Dim rows = LoadAll()
                Dim result = mutator(rows)
                Persist(rows)
                Return result
            End SyncLock
        End Function

        Public Function All() As List(Of T)
            SyncLock _gate
                Return LoadAll()
            End SyncLock
        End Function

        Public Function Count() As Integer
            SyncLock _gate
                Using cmd = _ds.CreateCommand($"SELECT COUNT(*) FROM ""{_table}""")
                    Return Convert.ToInt32(cmd.ExecuteScalar())
                End Using
            End SyncLock
        End Function

        ' Replace the whole table with the given rows, transactionally.
        Private Sub Persist(rows As List(Of T))
            Using conn = _ds.OpenConnection()
                Using tx = conn.BeginTransaction()
                    Using del = New NpgsqlCommand($"DELETE FROM ""{_table}""", conn, tx)
                        del.ExecuteNonQuery()
                    End Using
                    If rows.Count > 0 Then
                        Dim colList = String.Join(", ", _cols.Select(Function(c) $"""{c}"""))
                        Dim paramList = String.Join(", ", Enumerable.Range(0, _props.Length).Select(Function(i) "@p" & i))
                        Dim sql = $"INSERT INTO ""{_table}"" ({colList}) VALUES ({paramList})"
                        For Each row In rows
                            Using cmd = New NpgsqlCommand(sql, conn, tx)
                                For i = 0 To _props.Length - 1
                                    Dim v = _props(i).GetValue(row)
                                    cmd.Parameters.AddWithValue("p" & i, If(v, DBNull.Value))
                                Next
                                cmd.ExecuteNonQuery()
                            End Using
                        Next
                    End If
                    tx.Commit()
                End Using
            End Using
        End Sub

        Private Shared Function PgType(t As Type) As NpgsqlDbType
            t = If(Nullable.GetUnderlyingType(t), t)
            Select Case Type.GetTypeCode(t)
                Case TypeCode.Int32 : Return NpgsqlDbType.Integer
                Case TypeCode.Int64 : Return NpgsqlDbType.Bigint
                Case TypeCode.Boolean : Return NpgsqlDbType.Boolean
                Case TypeCode.Double, TypeCode.Single : Return NpgsqlDbType.Double
                Case Else : Return NpgsqlDbType.Text
            End Select
        End Function

    End Class

End Namespace
