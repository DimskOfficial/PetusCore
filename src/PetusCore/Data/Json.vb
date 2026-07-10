Imports System.Text.Json
Imports System.Text.Json.Serialization

Namespace Data

    ''' <summary>
    ''' Shared JSON serializer options for REST API responses (camelCase output).
    ''' Storage no longer uses JSON — see PgTable(Of T) for the relational layer.
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

End Namespace
