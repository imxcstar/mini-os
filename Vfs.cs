
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
            if (!Children.TryGetValue(name, out var node))
                throw new InvalidOperationException($"No such node {name}");
            Children.Remove(name);
            node.Parent = null;
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
        public void Rename(string path, string newName, DirectoryNode? cwd = null)
        {
            if (string.IsNullOrWhiteSpace(newName))
                throw new ArgumentException("newName");
            var (dir, leaf) = ResolveParent(path, cwd);
            if (!dir.Children.TryGetValue(leaf, out var node))
                throw new InvalidOperationException($"No such path: {path}");
            if (dir.Children.ContainsKey(newName))
                throw new InvalidOperationException($"Target '{newName}' already exists");
            dir.Remove(leaf);
            node.Name = newName;
            dir.Add(node);
        }
        public void Move(string source, string destination, DirectoryNode? cwd = null)
        {
            var node = Resolve(source, cwd);
            if (node == _root) throw new InvalidOperationException("Cannot move root");
            var (destDir, destLeaf) = ResolveParent(destination, cwd);
            if (destDir == node)
                throw new InvalidOperationException("Cannot move directory into itself");
            if (node is DirectoryNode dirNode && IsDescendant(dirNode, destDir))
                throw new InvalidOperationException("Cannot move directory into its subtree");
            if (destDir.Children.ContainsKey(destLeaf))
                throw new InvalidOperationException($"Destination '{destLeaf}' already exists");
            if (node.Parent is not DirectoryNode parent)
                throw new InvalidOperationException("Node has no parent");
            parent.Remove(node.Name);
            node.Name = destLeaf;
            destDir.Add(node);
        }
        public void Copy(string source, string destination, DirectoryNode? cwd = null)
        {
            var node = Resolve(source, cwd);
            var (destDir, destLeaf) = ResolveParent(destination, cwd);
            if (node is DirectoryNode dirNode && IsDescendant(dirNode, destDir))
                throw new InvalidOperationException("Cannot copy directory into its subtree");
            if (destDir.Children.ContainsKey(destLeaf))
                throw new InvalidOperationException($"Destination '{destLeaf}' already exists");
            var clone = CloneNode(node, destLeaf);
            destDir.Add(clone);
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
        public bool Exists(string path, DirectoryNode? cwd = null)
        {
            try
            {
                Resolve(path, cwd);
                return true;
            }
            catch
            {
                return false;
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

        private static bool IsDescendant(VNode ancestor, VNode node)
        {
            VNode? current = node;
            while (current != null)
            {
                if (current == ancestor) return true;
                current = current.Parent;
            }
            return false;
        }

        private static VNode CloneNode(VNode node, string name)
        {
            if (node is FileNode file)
            {
                return new FileNode(name) { Data = file.Data.ToArray() };
            }
            if (node is DirectoryNode dir)
            {
                var newDir = new DirectoryNode(name);
                foreach (var child in dir.Children.Values.OrderBy(c => c.Name, StringComparer.Ordinal))
                {
                    newDir.Add(CloneNode(child, child.Name));
                }
                return newDir;
            }
            throw new InvalidOperationException("Unknown node type");
        }
    }
}
