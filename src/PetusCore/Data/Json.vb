Imports System.IO
Imports System.Text.Json
Imports System.Text.Json.Serialization

Namespace Data

    ''' <summary>
    ''' Shared JSON serializer options. camelCase on disk, case-insensitive on
    ''' read, indented for human-readable database files.
    ''' </summary>
    Public Module Json
        Public ReadOnly Options As New JsonSerializerOptions With {
            .PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            .PropertyNameCaseInsensitive = True,
            .WriteIndented = True,
            .DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            .NumberHandling = JsonNumberHandling.AllowReadingFromString
        }

        Public Function Serialize(Of T)(value As T) As String
            Return JsonSerializer.Serialize(value, Options)
        End Function

        Public Function Deserialize(Of T)(text As String) As T
            Return JsonSerializer.Deserialize(Of T)(text, Options)
        End Function
    End Module

    ''' <summary>
    ''' A single JSON-backed table. Holds its rows in memory and persists the
    ''' whole file atomically on every mutation. All access is guarded by a
    ''' per-table lock so concurrent web requests stay consistent.
    ''' </summary>
    Public Class JsonTable(Of T)

        Private ReadOnly _path As String
        Private ReadOnly _gate As New Object()
        Private _rows As List(Of T)

        Public Sub New(path As String)
            _path = path
            Load()
        End Sub

        Private Sub Load()
            SyncLock _gate
                If File.Exists(_path) Then
                    Try
                        Dim text = File.ReadAllText(_path)
                        _rows = If(Json.Deserialize(Of List(Of T))(text), New List(Of T)())
                    Catch
                        _rows = New List(Of T)()
                    End Try
                Else
                    _rows = New List(Of T)()
                End If
            End SyncLock
        End Sub

        Private Sub Persist()
            ' Caller already holds _gate.
            Dim tmp = _path & ".tmp"
            File.WriteAllText(tmp, Json.Serialize(_rows))
            If File.Exists(_path) Then File.Delete(_path)
            File.Move(tmp, _path)
        End Sub

        ''' <summary>Read a value from the table under lock (returns a computed result).</summary>
        Public Function Read(Of R)(reader As Func(Of List(Of T), R)) As R
            SyncLock _gate
                Return reader(_rows)
            End SyncLock
        End Function

        ''' <summary>Mutate the table under lock, then persist to disk.</summary>
        Public Sub Write(mutator As Action(Of List(Of T)))
            SyncLock _gate
                mutator(_rows)
                Persist()
            End SyncLock
        End Sub

        ''' <summary>Mutate the table under lock, persist, and return a value.</summary>
        Public Function WriteReturn(Of R)(mutator As Func(Of List(Of T), R)) As R
            SyncLock _gate
                Dim result = mutator(_rows)
                Persist()
                Return result
            End SyncLock
        End Function

        ''' <summary>Snapshot copy of all rows (safe to enumerate outside the lock).</summary>
        Public Function All() As List(Of T)
            SyncLock _gate
                Return New List(Of T)(_rows)
            End SyncLock
        End Function

        Public Function Count() As Integer
            SyncLock _gate
                Return _rows.Count
            End SyncLock
        End Function

    End Class

End Namespace
