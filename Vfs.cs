
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MiniOS
{
    public abstract class VNode
    {
        public string Name { get; set; }
        public DirectoryNode? Parent { get; set; }
        protected VNode(string name) => Name = name;
        public string Path
        {
            get
            {
                if (Parent == null) return "/";
                var parts = new List<string>();
                VNode? n = this;
                while (n != null && n.Parent != null) { parts.Add(n.Name); n = n.Parent; }
                parts.Reverse();
                return "/" + string.Join("/", parts);
            }
        }
    }

    public class FileNode : VNode
    {
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public FileNode(string name) : base(name) { }
    }

    public class DirectoryNode : VNode
    {
        public Dictionary<string, VNode> Children { get; } = new(StringComparer.Ordinal);
        public DirectoryNode(string name) : base(name) { }

        public bool TryGet(string name, out VNode node) => Children.TryGetValue(name, out node!);
        public void Add(VNode node)
        {
            if (Children.ContainsKey(node.Name)) throw new InvalidOperationException($"Node {node.Name} already exists");
            node.Parent = this;
            Children[node.Name] = node;
        }
        public void Remove(string name)
        {
            if (!Children.Remove(name)) throw new InvalidOperationException($"No such node {name}");
        }
    }

    public class Vfs
    {
        private readonly DirectoryNode _root = new DirectoryNode("/");
        public DirectoryNode Root => _root;

        public void Mkdir(string path, DirectoryNode? cwd = null)
        {
            var (dir, leaf) = ResolveParent(path, cwd);
            dir.Add(new DirectoryNode(leaf));
        }
        public void Touch(string path, DirectoryNode? cwd = null)
        {
            var (dir, leaf) = ResolveParent(path, cwd);
            if (dir.Children.ContainsKey(leaf)) return;
            dir.Add(new FileNode(leaf));
        }
        public void Remove(string path, DirectoryNode? cwd = null)
        {
            var (dir, leaf) = ResolveParent(path, cwd);
            dir.Remove(leaf);
        }
        public void WriteAllText(string path, string text, DirectoryNode? cwd = null)
        {
            WriteAllBytes(path, Encoding.UTF8.GetBytes(text), cwd);
        }
        public void WriteAllBytes(string path, byte[] data, DirectoryNode? cwd = null)
        {
            var (dir, leaf) = ResolveParent(path, cwd);
            if (!dir.Children.TryGetValue(leaf, out var n))
            {
                var f = new FileNode(leaf) { Data = data };
                dir.Add(f);
            }
            else if (n is FileNode f)
            {
                f.Data = data;
            }
            else throw new InvalidOperationException("Cannot write to directory");
        }
        public string ReadAllText(string path, DirectoryNode? cwd = null)
        {
            var bytes = ReadAllBytes(path, cwd);
            return Encoding.UTF8.GetString(bytes);
        }
        public byte[] ReadAllBytes(string path, DirectoryNode? cwd = null)
        {
            var n = Resolve(path, cwd);
            if (n is FileNode f) return f.Data;
            throw new InvalidOperationException("not a file");
        }
        public IEnumerable<(string name, bool isDir, long size)> List(string path, DirectoryNode? cwd = null)
        {
            var n = Resolve(path, cwd);
            if (n is not DirectoryNode d) throw new InvalidOperationException("not a directory");
            foreach (var kv in d.Children.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                if (kv.Value is FileNode f) yield return (kv.Key, false, f.Data.LongLength);
                else yield return (kv.Key, true, 0);
            }
        }
        public DirectoryNode GetCwd(string path, DirectoryNode? cwd = null)
        {
            var n = Resolve(path, cwd);
            if (n is DirectoryNode d) return d;
            throw new InvalidOperationException("not a directory");
        }

        private (DirectoryNode dir, string leaf) ResolveParent(string path, DirectoryNode? cwd = null)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path");
            var isAbs = path.StartsWith("/");
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries).ToList();
            var leaf = parts.Last();
            parts.RemoveAt(parts.Count - 1);
            DirectoryNode cur = isAbs ? _root : (cwd ?? _root);
            foreach (var part in parts)
            {
                if (part == ".") continue;
                if (part == "..") { cur = cur.Parent ?? _root; continue; }
                if (!cur.TryGet(part, out var n)) throw new InvalidOperationException($"No such directory: {part}");
                if (n is not DirectoryNode d) throw new InvalidOperationException($"{part} is not a directory");
                cur = d;
            }
            return (cur, leaf);
        }
        private VNode Resolve(string path, DirectoryNode? cwd = null)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("path");
            if (path == "/") return _root;
            var isAbs = path.StartsWith("/");
            var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
            DirectoryNode cur = isAbs ? _root : (cwd ?? _root);
            foreach (var part in parts)
            {
                if (part == ".") continue;
                if (part == "..") { cur = cur.Parent ?? _root; continue; }
                if (!cur.TryGet(part, out var n)) throw new InvalidOperationException($"No such path: {path}");
                if (n is DirectoryNode d) { cur = d; }
                else if (n is FileNode f && part != parts.Last())
                    throw new InvalidOperationException($"'{n.Name}' is not a directory");
                else if (n is FileNode && part == parts.Last()) return n;
                if (part == parts.Last()) return n;
            }
            return cur;
        }
    }
}
