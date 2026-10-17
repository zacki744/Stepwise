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
        "tree" => CreateTree(payload),
        _ => throw new ArgumentException($"Unknown datasetType '{datasetType}'")
    };

    // Besides the JSON shapes below, each dataset type accepts {"text": "..."}; see TextImport for the formats.
    private static bool TryGetText(JsonElement payload, out string text)
    {
        text = "";
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty("text", out var element)) return false;
        if (element.ValueKind != JsonValueKind.String) throw new ArgumentException("text must be a string");
        text = element.GetString()!;
        return true;
    }

    private static void RejectAlongsideText(JsonElement payload, params string[] others)
    {
        foreach (var name in others)
            if (payload.TryGetProperty(name, out _))
                throw new ArgumentException($"Give either text or {name}, not both");
    }

    private static Matrix CreateMatrix(JsonElement payload)
    {
        if (TryGetText(payload, out var gridText))
        {
            RejectAlongsideText(payload, "rows", "columns", "cells");
            return TextImport.ParseMatrix(gridText);
        }

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

    // Three accepted shapes: nothing at all (an empty tree), {"values": [..]} (inserted in order), or an
    // exact structure {"root": n, "slotCount": n?, "nodes": [[slot, value, left, right], ..]} as a snapshot sends it.
    private static Tree CreateTree(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object) return Tree.Empty();

        if (TryGetText(payload, out var valuesText))
        {
            RejectAlongsideText(payload, "values", "nodes");
            return TextImport.ParseTree(valuesText);
        }

        if (payload.TryGetProperty("values", out var values))
        {
            if (values.ValueKind != JsonValueKind.Array) throw new ArgumentException("values must be an array of integers");
            return Tree.FromValues(values.EnumerateArray().Select(v => v.GetInt32()));
        }

        if (payload.TryGetProperty("nodes", out var nodes))
        {
            if (nodes.ValueKind != JsonValueKind.Array) throw new ArgumentException("nodes must be an array");
            if (!payload.TryGetProperty("root", out var root)) throw new ArgumentException("A tree with nodes needs a root");

            var parsed = nodes.EnumerateArray().Select(ParseTreeNode).ToList();
            int? slotCount = payload.TryGetProperty("slotCount", out var slots) ? slots.GetInt32() : null;
            return Tree.FromNodes(root.GetInt32(), parsed, slotCount);
        }

        return Tree.Empty();
    }

    private static TreeNodeData ParseTreeNode(JsonElement node)
    {
        if (node.ValueKind != JsonValueKind.Array || node.GetArrayLength() != 4)
            throw new ArgumentException("Each tree node must be [slot, value, left, right]");
        return new TreeNodeData(node[0].GetInt32(), node[1].GetInt32(), node[2].GetInt32(), node[3].GetInt32());
    }

    private static Graph CreateGraph(JsonElement payload)
    {
        if (TryGetText(payload, out var graphText))
        {
            RejectAlongsideText(payload, "edges");
            var format = payload.TryGetProperty("format", out var f)
                ? (f.ValueKind == JsonValueKind.String ? f.GetString()! : throw new ArgumentException("format must be a string"))
                : "edgeList";
            bool directed = payload.TryGetProperty("directed", out var d) && d.GetBoolean();
            int? declared = payload.TryGetProperty("nodeCount", out var n) && n.ValueKind != JsonValueKind.Null ? n.GetInt32() : null;
            return TextImport.ParseGraph(graphText, format, directed, declared);
        }

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