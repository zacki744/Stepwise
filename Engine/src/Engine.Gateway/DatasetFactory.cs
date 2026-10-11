using System.Text.Json;
using Engine.Domain;
using Engine.Domain.Datasets;

namespace Engine.Gateway;

public static class DatasetFactory
{
    public static IDataset Create(string datasetType, JsonElement payload) => datasetType switch
    {
        "matrix" => CreateMatrix(payload),
        "graph" => CreateGraph(payload),
        "tree" => Tree.Empty(),
        _ => throw new ArgumentException($"Unknown datasetType '{datasetType}'")
    };

    private static Matrix CreateMatrix(JsonElement payload)
    {
        // The constructor enforces the size limit, so an oversized matrix is rejected before any cell is read.
        var matrix = new Matrix(payload.GetProperty("rows").GetInt32(), payload.GetProperty("columns").GetInt32());

        // Optional "cells": row-major, exactly rows*columns integers. Absent means all zeros.
        if (payload.TryGetProperty("cells", out var cells) && cells.ValueKind != JsonValueKind.Null)
        {
            long expected = (long)matrix.Rows * matrix.Columns;
            if (cells.ValueKind != JsonValueKind.Array || cells.GetArrayLength() != expected)
                throw new ArgumentException($"cells must be an array of exactly {expected} integers (rows*columns, row-major)");

            int i = 0;
            foreach (var cell in cells.EnumerateArray())
            {
                matrix[i / matrix.Columns, i % matrix.Columns] = cell.GetInt32();
                i++;
            }
        }
        return matrix;
    }

    private static Graph CreateGraph(JsonElement payload)
    {
        int nodeCount = payload.GetProperty("nodeCount").GetInt32();
        var edges = payload.GetProperty("edges").EnumerateArray().Select(ParseEdge);
        return new Graph(nodeCount, edges);
    }

    private static (int From, int To, double Weight) ParseEdge(JsonElement edge)
    {
        if (edge.ValueKind != JsonValueKind.Array || edge.GetArrayLength() < 2)
            throw new ArgumentException("Each edge must be an array [from, to] or [from, to, weight]");

        return (edge[0].GetInt32(), edge[1].GetInt32(), edge.GetArrayLength() > 2 ? edge[2].GetDouble() : 1.0);
    }
}