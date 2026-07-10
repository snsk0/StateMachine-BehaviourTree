using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace HFSM.Editor
{
    public sealed class StateMachineGraphEditorWindow : EditorWindow
    {
        private static readonly Vector2 NodeSize = new Vector2(170f, 72f);
        private const float InspectorWidth = 340f;

        private StateMachineGraph _graph;
        private StateMachineGraphController _controller;
        private string _selectedNodeId;

        private Vector2 _panOffset = new Vector2(40f, 40f);

        private bool _isNodeDragging;
        private string _dragNodeId;
        private Vector2 _dragMouseOrigin;
        private Vector2 _dragNodeOrigin;

        private bool _isCanvasDragging;
        private Vector2 _canvasDragMouseOrigin;
        private Vector2 _canvasPanOrigin;

        private int _newTransitionTargetIndex;
        private int _newTransitionEventId;
        private int _newTransitionPriority;

        private string _renamingNodeId;
        private string _renameBuffer;
        private bool _focusRenameField;

        private static List<Type> _cachedStateTypes;
        private static string[] _cachedStateTypeLabels;
        private static List<Type> _cachedContextTypes;
        private static string[] _cachedContextTypeLabels;

        private int _newGraphContextTypeIndex;
        private readonly HashSet<string> _collapsedNodeIds = new HashSet<string>();

        [MenuItem("Tools/HFSM/State Machine Graph Editor")]
        private static void Open()
        {
            GetWindow<StateMachineGraphEditorWindow>("HFSM Graph");
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (_graph == null)
            {
                EditorGUILayout.HelpBox("Select a StateMachineGraph asset or create a new one.", MessageType.Info);
                return;
            }

            var canvasRect = new Rect(0f, 22f, position.width - InspectorWidth, position.height - 22f);
            var inspectorRect = new Rect(position.width - InspectorWidth, 22f, InspectorWidth, position.height - 22f);

            HandleCanvasInput(canvasRect);
            DrawCanvas(canvasRect);
            DrawInspector(inspectorRect);
        }

        private void DrawToolbar()
        {
            EnsureStateTypeCache();
            EnsureContextTypeCache();

            using (new GUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var selected = (StateMachineGraph)EditorGUILayout.ObjectField(
                    _graph,
                    typeof(StateMachineGraph),
                    false,
                    GUILayout.Width(260f));

                if (selected != _graph)
                {
                    BindGraph(selected);
                }

                if (_graph != null)
                {
                    DrawGraphContextPopup();
                }
                else
                {
                    DrawNewGraphContextPopup();
                }

                if (GUILayout.Button("New Graph", EditorStyles.toolbarButton, GUILayout.Width(84f)))
                {
                    CreateGraphAsset();
                }

                if (_graph != null && GUILayout.Button("Add Node", EditorStyles.toolbarButton, GUILayout.Width(72f)))
                {
                    _controller.AddNode(new Vector2(80f, 80f));
                }

                GUILayout.FlexibleSpace();
            }
        }

        private void DrawCanvas(Rect canvasRect)
        {
            EditorGUI.DrawRect(canvasRect, new Color(0.10f, 0.10f, 0.11f, 1f));
            DrawGrid(canvasRect, 24f, new Color(1f, 1f, 1f, 0.05f));
            DrawGrid(canvasRect, 96f, new Color(1f, 1f, 1f, 0.11f));

            GUI.BeginGroup(canvasRect);
            DrawSubMachineWindows();
            DrawTransitions();
            DrawNodes();
            GUI.EndGroup();
        }

        private void DrawGrid(Rect rect, float spacing, Color color)
        {
            Handles.BeginGUI();
            Handles.color = color;

            float offsetX = _panOffset.x % spacing;
            float offsetY = _panOffset.y % spacing;

            for (float x = rect.x + offsetX; x < rect.xMax; x += spacing)
            {
                Handles.DrawLine(new Vector3(x, rect.y), new Vector3(x, rect.yMax));
            }

            for (float y = rect.y + offsetY; y < rect.yMax; y += spacing)
            {
                Handles.DrawLine(new Vector3(rect.x, y), new Vector3(rect.xMax, y));
            }

            Handles.color = Color.white;
            Handles.EndGUI();
        }

        private void DrawSubMachineWindows()
        {
            foreach (var parent in _graph.Nodes)
            {
                var children = _graph.Nodes.Where(n => NormalizeNodeId(n.ParentNodeId) == parent.Id).ToList();
                if (children.Count == 0 || IsCollapsed(parent.Id) || !AreAncestorsExpanded(parent))
                {
                    continue;
                }

                var rect = GetSubMachineContainerRect(parent, children);
                bool isSelected = _selectedNodeId == parent.Id;
                bool isRootEntry = GetEffectiveRootEntryNodeId() == parent.Id;
                bool isHierarchyEntry = IsEffectiveInitialOfParent(parent.Id);

                EditorGUI.DrawRect(rect, Color.white);
                DrawRectOutline(rect, Color.black, 1f);

                // Parent node and sub-machine area are shown as one expanded block.
                string subMachineLabel = "SubMachine";
                if (isRootEntry)
                {
                    subMachineLabel += " [RootEntry]";
                }
                if (isHierarchyEntry)
                {
                    subMachineLabel += " [Entry]";
                }

                var subMachineLabelStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel);
                subMachineLabelStyle.normal.textColor = Color.black;
                GUI.Label(new Rect(rect.x + 8f, rect.y + 4f, rect.width - 16f, 16f), subMachineLabel, subMachineLabelStyle);
                if (GUI.Button(new Rect(rect.xMax - 70f, rect.y + 2f, 64f, 18f), "まとめる", EditorStyles.miniButton))
                {
                    CollapseNode(parent.Id);
                    GUIUtility.ExitGUI();
                }
            }
        }

        private void DrawNodes()
        {
            foreach (var node in _graph.Nodes)
            {
                if (!IsNodeVisible(node))
                {
                    continue;
                }

                var rect = new Rect(node.Position + _panOffset, NodeSize);
                bool isSelected = _selectedNodeId == node.Id;
                bool isRootEntry = GetEffectiveRootEntryNodeId() == node.Id;
                bool isHierarchyEntry = IsEffectiveInitialOfParent(node.Id);
                bool hasChildren = _graph.Nodes.Any(n => NormalizeNodeId(n.ParentNodeId) == node.Id);

                EditorGUI.DrawRect(rect, isSelected ? new Color(0.18f, 0.36f, 0.58f) : new Color(0.20f, 0.20f, 0.21f));
                GUI.Box(rect, GUIContent.none);

                string title = node.Name;
                if (isRootEntry)
                {
                    title += " [RootEntry]";
                }
                if (isHierarchyEntry)
                {
                    title += " [Entry]";
                }
                var shortId = string.IsNullOrEmpty(node.Id)
                    ? "(no id)"
                    : node.Id.Substring(0, Mathf.Min(8, node.Id.Length));
                var titleRect = new Rect(rect.x + 8f, rect.y + 8f, rect.width - 16f, 18f);
                if (_renamingNodeId == node.Id)
                {
                    GUI.SetNextControlName("StateNodeRenameField");
                    _renameBuffer = EditorGUI.TextField(titleRect, _renameBuffer ?? node.Name);
                }
                else
                {
                    GUI.Label(titleRect, title, EditorStyles.boldLabel);
                }

                GUI.Label(new Rect(rect.x + 8f, rect.y + 30f, rect.width - 16f, 16f), shortId, EditorStyles.miniLabel);

                int outCount = CountOutgoingTransitions(node.Id);
                int inCount = CountIncomingTransitions(node.Id);
                string hierarchyHint = hasChildren ? "SubSM" : "State";
                GUI.Label(
                    new Rect(rect.x + 8f, rect.y + 48f, rect.width - 16f, 16f),
                    $"{hierarchyHint}  OUT:{outCount} IN:{inCount}",
                    EditorStyles.miniLabel);

                if (hasChildren && IsCollapsed(node.Id))
                {
                    if (GUI.Button(new Rect(rect.xMax - 58f, rect.y + 6f, 50f, 18f), "展開", EditorStyles.miniButton))
                    {
                        ExpandNode(node.Id);
                        GUIUtility.ExitGUI();
                    }
                }
            }

            if (_focusRenameField)
            {
                EditorGUI.FocusTextInControl("StateNodeRenameField");
                _focusRenameField = false;
            }
        }

        private void DrawTransitions()
        {
            var visibleNodes = _graph.Nodes.Where(IsNodeVisible).ToList();
            var nodeRects = visibleNodes.ToDictionary(n => n.Id, GetNodeAnchorRect);

            Handles.BeginGUI();
            foreach (var transition in _graph.Transitions)
            {
                if (!nodeRects.TryGetValue(transition.FromNodeId, out var fromRect))
                {
                    continue;
                }

                if (!nodeRects.TryGetValue(transition.ToNodeId, out var toRect))
                {
                    continue;
                }

                GetTransitionAnchors(fromRect, toRect, out var start, out var end, out var startTangent, out var endTangent);

                Handles.DrawBezier(start, end, startTangent, endTangent, new Color(0.9f, 0.9f, 0.9f), null, 2f);
                DrawArrowHead(end, end - endTangent, 12f, 24f);
            }
            Handles.EndGUI();
        }

        private Rect GetChildrenBounds(List<StateNodeData> children)
        {
            var firstRect = GetNodeAnchorRect(children[0]);
            float minX = firstRect.xMin;
            float minY = firstRect.yMin;
            float maxX = firstRect.xMax;
            float maxY = firstRect.yMax;

            for (int i = 1; i < children.Count; i++)
            {
                var rect = GetNodeAnchorRect(children[i]);
                minX = Mathf.Min(minX, rect.xMin);
                minY = Mathf.Min(minY, rect.yMin);
                maxX = Mathf.Max(maxX, rect.xMax);
                maxY = Mathf.Max(maxY, rect.yMax);
            }

            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        private Rect GetNodeAnchorRect(StateNodeData node)
        {
            var children = _graph.Nodes.Where(n => NormalizeNodeId(n.ParentNodeId) == node.Id).ToList();
            if (children.Count == 0 || IsCollapsed(node.Id))
            {
                return new Rect(node.Position + _panOffset, NodeSize);
            }

            return GetSubMachineContainerRect(node, children);
        }

        private Rect GetNodeAnchorRectModel(StateNodeData node, Dictionary<string, Vector2> overrides = null)
        {
            var children = _graph.Nodes.Where(n => NormalizeNodeId(n.ParentNodeId) == node.Id).ToList();
            var nodePosition = GetNodePosition(node, overrides);
            var nodeRect = new Rect(nodePosition, NodeSize);
            if (children.Count == 0 || IsCollapsed(node.Id))
            {
                return nodeRect;
            }

            var childrenRect = GetChildrenBoundsModel(children, overrides);
            return ExpandRect(UnionRect(nodeRect, childrenRect), 18f, 18f, 18f, 18f);
        }

        private static Rect UnionRect(Rect a, Rect b)
        {
            float minX = Mathf.Min(a.xMin, b.xMin);
            float minY = Mathf.Min(a.yMin, b.yMin);
            float maxX = Mathf.Max(a.xMax, b.xMax);
            float maxY = Mathf.Max(a.yMax, b.yMax);
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        private static Rect ExpandRect(Rect rect, float left, float top, float right, float bottom)
        {
            return Rect.MinMaxRect(rect.xMin - left, rect.yMin - top, rect.xMax + right, rect.yMax + bottom);
        }

        private Rect GetSubMachineContainerRect(StateNodeData parent, List<StateNodeData> children)
        {
            var parentRect = new Rect(parent.Position + _panOffset, NodeSize);
            var childrenRect = GetChildrenBounds(children);
            return ExpandRect(UnionRect(parentRect, childrenRect), 18f, 18f, 18f, 18f);
        }

        private Rect GetChildrenBoundsModel(List<StateNodeData> children, Dictionary<string, Vector2> overrides = null)
        {
            var firstRect = GetNodeAnchorRectModel(children[0], overrides);
            float minX = firstRect.xMin;
            float minY = firstRect.yMin;
            float maxX = firstRect.xMax;
            float maxY = firstRect.yMax;

            for (int i = 1; i < children.Count; i++)
            {
                var rect = GetNodeAnchorRectModel(children[i], overrides);
                minX = Mathf.Min(minX, rect.xMin);
                minY = Mathf.Min(minY, rect.yMin);
                maxX = Mathf.Max(maxX, rect.xMax);
                maxY = Mathf.Max(maxY, rect.yMax);
            }

            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        private StateNodeData FindSubMachineOwnerAt(Vector2 localMouse)
        {
            for (int i = _graph.Nodes.Count - 1; i >= 0; --i)
            {
                var parent = _graph.Nodes[i];
                var children = _graph.Nodes.Where(n => NormalizeNodeId(n.ParentNodeId) == parent.Id).ToList();
                if (children.Count == 0 || IsCollapsed(parent.Id) || !AreAncestorsExpanded(parent))
                {
                    continue;
                }

                var containerRect = GetSubMachineContainerRect(parent, children);
                if (containerRect.Contains(localMouse))
                {
                    return parent;
                }
            }

            return null;
        }

        private static void GetTransitionAnchors(
            Rect fromRect,
            Rect toRect,
            out Vector3 start,
            out Vector3 end,
            out Vector3 startTangent,
            out Vector3 endTangent)
        {
            if (fromRect.center.x <= toRect.center.x)
            {
                start = new Vector3(fromRect.xMax, fromRect.center.y);
                end = new Vector3(toRect.xMin, toRect.center.y);
                startTangent = start + Vector3.right * 48f;
                endTangent = end + Vector3.left * 48f;
                return;
            }

            start = new Vector3(fromRect.xMin, fromRect.center.y);
            end = new Vector3(toRect.xMax, toRect.center.y);
            startTangent = start + Vector3.left * 48f;
            endTangent = end + Vector3.right * 48f;
        }

        private static void DrawArrowHead(Vector3 tip, Vector3 direction, float size, float angleDeg)
        {
            if (direction.sqrMagnitude <= Mathf.Epsilon)
            {
                return;
            }

            var dir = direction.normalized;
            var left = Quaternion.Euler(0f, 0f, angleDeg) * (-dir) * size;
            var right = Quaternion.Euler(0f, 0f, -angleDeg) * (-dir) * size;
            Handles.DrawLine(tip, tip + left);
            Handles.DrawLine(tip, tip + right);
        }

        private void HandleCanvasInput(Rect canvasRect)
        {
            var evt = Event.current;
            if (!canvasRect.Contains(evt.mousePosition))
            {
                return;
            }

            var localMouse = evt.mousePosition - canvasRect.position;
            var hitNode = FindNodeAt(localMouse);

            if (!string.IsNullOrEmpty(_renamingNodeId))
            {
                if (evt.type == EventType.KeyDown)
                {
                    if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                    {
                        CommitNodeRename();
                        evt.Use();
                        return;
                    }

                    if (evt.keyCode == KeyCode.Escape)
                    {
                        CancelNodeRename();
                        evt.Use();
                        return;
                    }
                }

                if (evt.type == EventType.MouseDown && evt.button == 0)
                {
                    var renamingNode = _graph.FindNode(_renamingNodeId);
                    if (renamingNode == null)
                    {
                        CancelNodeRename();
                    }
                    else
                    {
                        var renamingRect = new Rect(renamingNode.Position + _panOffset, NodeSize);
                        var renamingTitleRect = new Rect(renamingRect.x + 8f, renamingRect.y + 8f, renamingRect.width - 16f, 18f);
                        if (!renamingTitleRect.Contains(localMouse))
                        {
                            CommitNodeRename();
                        }
                    }
                }
            }

            if (evt.type == EventType.ContextClick)
            {
                var menu = new GenericMenu();
                var nodePos = localMouse - _panOffset;
                menu.AddItem(new GUIContent("Add State"), false, () => _controller.AddNode(nodePos));
                if (hitNode != null)
                {
                    menu.AddItem(new GUIContent("Rename"), false, () => StartNodeRename(hitNode));
                }
                menu.ShowAsContext();
                evt.Use();
                return;
            }

            if (evt.type == EventType.MouseDown && evt.button == 0)
            {
                if (hitNode != null)
                {
                    _selectedNodeId = hitNode.Id;
                    if (evt.clickCount == 2)
                    {
                        StartNodeRename(hitNode);
                        evt.Use();
                        return;
                    }

                    if (_renamingNodeId == hitNode.Id)
                    {
                        evt.Use();
                        return;
                    }

                    _isNodeDragging = true;
                    _dragNodeId = hitNode.Id;
                    _dragMouseOrigin = localMouse;
                    _dragNodeOrigin = hitNode.Position;
                    evt.Use();
                    return;
                }

                var subMachineOwner = FindSubMachineOwnerAt(localMouse);
                if (subMachineOwner != null)
                {
                    _selectedNodeId = subMachineOwner.Id;
                    _isNodeDragging = true;
                    _dragNodeId = subMachineOwner.Id;
                    _dragMouseOrigin = localMouse;
                    _dragNodeOrigin = subMachineOwner.Position;
                    evt.Use();
                    return;
                }

                _selectedNodeId = null;
            }

            if (evt.type == EventType.MouseDrag && evt.button == 0 && _isNodeDragging)
            {
                var delta = localMouse - _dragMouseOrigin;
                var desired = _dragNodeOrigin + delta;
                var resolved = ResolveDraggedNodePosition(_dragNodeId, desired);
                _controller.MoveNode(_dragNodeId, resolved);
                Repaint();
                evt.Use();
                return;
            }

            if (evt.type == EventType.MouseUp && evt.button == 0)
            {
                _isNodeDragging = false;
                _dragNodeId = null;
            }

            if (evt.type == EventType.MouseDown && evt.button == 2)
            {
                _isCanvasDragging = true;
                _canvasDragMouseOrigin = evt.mousePosition;
                _canvasPanOrigin = _panOffset;
                evt.Use();
                return;
            }

            if (evt.type == EventType.MouseDrag && evt.button == 2 && _isCanvasDragging)
            {
                _panOffset = _canvasPanOrigin + (evt.mousePosition - _canvasDragMouseOrigin);
                Repaint();
                evt.Use();
                return;
            }

            if (evt.type == EventType.MouseUp && evt.button == 2)
            {
                _isCanvasDragging = false;
            }
        }

        private StateNodeData FindNodeAt(Vector2 localMouse)
        {
            for (int i = _graph.Nodes.Count - 1; i >= 0; --i)
            {
                var node = _graph.Nodes[i];
                if (!IsNodeVisible(node))
                {
                    continue;
                }

                var rect = new Rect(node.Position + _panOffset, NodeSize);
                if (rect.Contains(localMouse))
                {
                    return node;
                }
            }

            return null;
        }

        private void DrawInspector(Rect rect)
        {
            GUILayout.BeginArea(rect, EditorStyles.helpBox);
            GUILayout.Label("Inspector", EditorStyles.boldLabel);

            var selectedNode = string.IsNullOrWhiteSpace(_selectedNodeId) ? null : _graph.FindNode(_selectedNodeId);
            if (selectedNode == null)
            {
                GUILayout.Label("Select a node from the canvas.", EditorStyles.wordWrappedLabel);
                DrawValidation();
                GUILayout.EndArea();
                return;
            }

            EditorGUILayout.LabelField("Node Id", selectedNode.Id);
            EditorGUILayout.LabelField("Node Path", GetNodePath(selectedNode.Id));

            EditorGUI.BeginChangeCheck();
            var newName = EditorGUILayout.TextField("Name", selectedNode.Name);
            if (EditorGUI.EndChangeCheck())
            {
                _controller.RenameNode(selectedNode.Id, newName);
            }

            DrawStateTypeSelector(selectedNode);
            DrawParentSelector(selectedNode);

            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Child"))
                {
                    _controller.AddNode(new Vector2(selectedNode.Position.x + 220f, selectedNode.Position.y + 120f), selectedNode.Id);
                }
            }

            bool hasChildren = _graph.Nodes.Any(n => n.ParentNodeId == selectedNode.Id);
            if (hasChildren)
            {
                EditorGUILayout.HelpBox("This node owns a SubMachine. You can and should assign State Type to this owner state.", MessageType.Info);
                DrawInitialChildSelector(selectedNode);
                using (new GUILayout.HorizontalScope())
                {
                    if (IsCollapsed(selectedNode.Id))
                    {
                        if (GUILayout.Button("SubMachineを展開"))
                        {
                            ExpandNode(selectedNode.Id);
                        }
                    }
                    else
                    {
                        if (GUILayout.Button("SubMachineをまとめる"))
                        {
                            CollapseNode(selectedNode.Id);
                        }
                    }
                }
            }

            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Set As Root Entry"))
                {
                    _controller.SetEntryNode(selectedNode.Id);
                }

                if (GUILayout.Button("Delete"))
                {
                    _controller.RemoveNode(selectedNode.Id);
                    _selectedNodeId = null;
                    GUIUtility.ExitGUI();
                }
            }

            DrawHierarchyEntryControls(selectedNode);

            EditorGUILayout.Space(10f);
            GUILayout.Label("Transitions", EditorStyles.boldLabel);

            int removeIndex = -1;
            for (int i = 0; i < _graph.Transitions.Count; i++)
            {
                var transition = _graph.Transitions[i];
                if (transition.FromNodeId != selectedNode.Id)
                {
                    continue;
                }

                var target = _graph.FindNode(transition.ToNodeId);
                var targetName = target != null ? GetNodePath(target.Id) : "(missing)";

                using (new GUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    GUILayout.Label("-> " + targetName + $"  ev:{transition.EventId}  pr:{transition.Priority}", EditorStyles.miniLabel);
                    if (GUILayout.Button("X", GUILayout.Width(24f)))
                    {
                        removeIndex = i;
                    }
                }
            }

            if (removeIndex >= 0)
            {
                _controller.RemoveTransitionAt(removeIndex);
            }

            EditorGUILayout.Space(4f);
            GUILayout.Label("Add Transition", EditorStyles.boldLabel);

            var candidates = _graph.Nodes.Where(n => n.Id != selectedNode.Id).ToList();
            if (candidates.Count == 0)
            {
                GUILayout.Label("No transition target candidates.", EditorStyles.miniLabel);
            }
            else
            {
                var names = candidates.Select(node => GetNodePath(node.Id)).ToArray();
                _newTransitionTargetIndex = Mathf.Clamp(_newTransitionTargetIndex, 0, names.Length - 1);
                _newTransitionTargetIndex = EditorGUILayout.Popup("Target", _newTransitionTargetIndex, names);
                _newTransitionEventId = EditorGUILayout.IntField("Event Id", _newTransitionEventId);
                _newTransitionPriority = EditorGUILayout.IntField("Priority", _newTransitionPriority);

                if (GUILayout.Button("Add Transition"))
                {
                    var targetNode = candidates[_newTransitionTargetIndex];
                    _controller.AddTransition(selectedNode.Id, targetNode.Id, _newTransitionEventId, _newTransitionPriority);
                }
            }

            EditorGUILayout.Space(10f);
            DrawValidation();
            GUILayout.EndArea();
        }

        private void DrawParentSelector(StateNodeData selectedNode)
        {
            var descendants = CollectDescendantIds(selectedNode.Id);
            var candidates = _graph.Nodes
                .Where(n => n.Id != selectedNode.Id && !descendants.Contains(n.Id))
                .ToList();

            var labels = new List<string> { "(Root)" };
            labels.AddRange(candidates.Select(n => GetNodePath(n.Id)));

            string currentParentId = NormalizeNodeId(selectedNode.ParentNodeId);
            int currentIndex = 0;
            if (!string.IsNullOrEmpty(currentParentId))
            {
                int idx = candidates.FindIndex(n => n.Id == currentParentId);
                currentIndex = idx >= 0 ? idx + 1 : 0;
            }

            int nextIndex = EditorGUILayout.Popup("Parent", currentIndex, labels.ToArray());
            if (nextIndex != currentIndex)
            {
                string newParentId = nextIndex == 0 ? null : candidates[nextIndex - 1].Id;
                _controller.ReparentNode(selectedNode.Id, newParentId);
            }
        }

        private static void DrawRectOutline(Rect rect, Color color, float thickness)
        {
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMax - thickness, rect.width, thickness), color);
            EditorGUI.DrawRect(new Rect(rect.xMin, rect.yMin, thickness, rect.height), color);
            EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.yMin, thickness, rect.height), color);
        }

        private void DrawInitialChildSelector(StateNodeData selectedNode)
        {
            var children = _graph.Nodes
                .Where(n => NormalizeNodeId(n.ParentNodeId) == selectedNode.Id)
                .ToList();
            if (children.Count == 0)
            {
                return;
            }

            var labels = new List<string> { "(First Child / Default)" };
            labels.AddRange(children.Select(n => n.Name));

            int currentIndex = 0;
            if (!string.IsNullOrWhiteSpace(selectedNode.InitialChildNodeId))
            {
                int idx = children.FindIndex(n => n.Id == selectedNode.InitialChildNodeId);
                if (idx >= 0)
                {
                    currentIndex = idx + 1;
                }
            }

            int nextIndex = EditorGUILayout.Popup("Entry State (Child)", currentIndex, labels.ToArray());
            if (nextIndex != currentIndex)
            {
                string childId = nextIndex == 0 ? null : children[nextIndex - 1].Id;
                _controller.SetInitialChildNode(selectedNode.Id, childId);
            }
        }

        private void DrawHierarchyEntryControls(StateNodeData selectedNode)
        {
            string parentId = NormalizeNodeId(selectedNode.ParentNodeId);
            if (string.IsNullOrEmpty(parentId))
            {
                return;
            }

            var parent = _graph.FindNode(parentId);
            if (parent == null)
            {
                return;
            }

            EditorGUILayout.Space(4f);
            GUILayout.Label("Hierarchy Entry", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Parent Hierarchy", parent.Name);

            bool alreadyEntry = IsEffectiveInitialOfParent(selectedNode.Id);
            using (new EditorGUI.DisabledScope(alreadyEntry))
            {
                if (GUILayout.Button("Set As Entry In This Hierarchy"))
                {
                    _controller.SetInitialChildNode(parent.Id, selectedNode.Id);
                }
            }
        }

        private void DrawStateTypeSelector(StateNodeData selectedNode)
        {
            EnsureStateTypeCache();
            EnsureContextTypeCache();

            var contextType = ResolveGraphContextType();
            var compatibleTypes = GetCompatibleStateTypes(contextType);
            var compatibleLabels = BuildStateTypeLabels(compatibleTypes);

            using (new GUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Refresh Types", GUILayout.Width(110f)))
                {
                    _cachedStateTypes = null;
                    _cachedStateTypeLabels = null;
                    _cachedContextTypes = null;
                    _cachedContextTypeLabels = null;
                    EnsureStateTypeCache();
                    EnsureContextTypeCache();
                    contextType = ResolveGraphContextType();
                    compatibleTypes = GetCompatibleStateTypes(contextType);
                    compatibleLabels = BuildStateTypeLabels(compatibleTypes);
                }
            }

            int currentIndex = 0;
            if (!string.IsNullOrWhiteSpace(selectedNode.StateTypeName))
            {
                int idx = compatibleTypes.FindIndex(t => t.AssemblyQualifiedName == selectedNode.StateTypeName);
                currentIndex = idx >= 0 ? idx + 1 : 0;
            }

            using (new EditorGUI.DisabledScope(contextType == null))
            {
                int nextIndex = EditorGUILayout.Popup("State Type", currentIndex, compatibleLabels);
                if (nextIndex != currentIndex)
                {
                    string typeName = nextIndex == 0 ? null : compatibleTypes[nextIndex - 1].AssemblyQualifiedName;
                    _controller.SetNodeStateType(selectedNode.Id, typeName);
                }
            }

            if (contextType == null)
            {
                EditorGUILayout.HelpBox("Set Graph Context first.", MessageType.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(selectedNode.StateTypeName))
            {
                EditorGUILayout.HelpBox("State Type is not assigned.", MessageType.Warning);
            }

            if (compatibleTypes.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No StateBase<> types are compatible with current Graph Context.",
                    MessageType.Info);
            }
        }

        private void DrawGraphContextPopup()
        {
            if (_cachedContextTypeLabels == null || _cachedContextTypeLabels.Length == 0)
            {
                GUILayout.Label("No Context Types", EditorStyles.miniLabel, GUILayout.Width(170f));
                return;
            }

            int currentIndex = 0;
            if (!string.IsNullOrWhiteSpace(_graph.ContextTypeName))
            {
                int idx = _cachedContextTypes.FindIndex(t => t.AssemblyQualifiedName == _graph.ContextTypeName);
                currentIndex = idx >= 0 ? idx : 0;
            }

            bool lockContext = _graph.Nodes.Count > 0 && !string.IsNullOrWhiteSpace(_graph.ContextTypeName);
            using (new EditorGUI.DisabledScope(lockContext))
            {
                int nextIndex = EditorGUILayout.Popup(
                    currentIndex,
                    _cachedContextTypeLabels,
                    EditorStyles.toolbarPopup,
                    GUILayout.Width(170f));

                if (nextIndex != currentIndex && nextIndex >= 0 && nextIndex < _cachedContextTypes.Count)
                {
                    _controller.SetGraphContextType(_cachedContextTypes[nextIndex].AssemblyQualifiedName);
                }
            }
        }

        private void DrawNewGraphContextPopup()
        {
            if (_cachedContextTypeLabels == null || _cachedContextTypeLabels.Length == 0)
            {
                GUILayout.Label("No Context Types", EditorStyles.miniLabel, GUILayout.Width(170f));
                return;
            }

            _newGraphContextTypeIndex = Mathf.Clamp(_newGraphContextTypeIndex, 0, _cachedContextTypeLabels.Length - 1);
            _newGraphContextTypeIndex = EditorGUILayout.Popup(
                _newGraphContextTypeIndex,
                _cachedContextTypeLabels,
                EditorStyles.toolbarPopup,
                GUILayout.Width(170f));
        }

        private Type ResolveGraphContextType()
        {
            if (_graph == null || string.IsNullOrWhiteSpace(_graph.ContextTypeName))
            {
                return null;
            }

            return Type.GetType(_graph.ContextTypeName, false);
        }

        private List<Type> GetCompatibleStateTypes(Type contextType)
        {
            var result = new List<Type>();
            if (contextType == null)
            {
                return result;
            }

            foreach (var stateType in _cachedStateTypes)
            {
                // Dropdown should list only concrete state types.
                if (stateType.IsGenericTypeDefinition || stateType.ContainsGenericParameters)
                {
                    continue;
                }

                if (TryResolveStateContextType(stateType, contextType, out var stateContextType)
                    && stateContextType == contextType)
                {
                    result.Add(stateType);
                }
            }

            return result;
        }

        private bool IsNodeVisible(StateNodeData node)
        {
            if (!AreAncestorsExpanded(node))
            {
                return false;
            }

            bool hasChildren = _graph.Nodes.Any(n => NormalizeNodeId(n.ParentNodeId) == node.Id);
            if (!hasChildren)
            {
                return true;
            }

            return IsCollapsed(node.Id);
        }

        private Vector2 ResolveDraggedNodePosition(string nodeId, Vector2 desiredPosition)
        {
            var node = _graph.FindNode(nodeId);
            if (node == null)
            {
                return desiredPosition;
            }

            var overrides = new Dictionary<string, Vector2> { [nodeId] = desiredPosition };
            if (!IsWithinParentSubMachineBounds(node, overrides))
            {
                return node.Position;
            }

            var movingRect = GetNodeAnchorRectModel(node, overrides);
            foreach (var other in _graph.Nodes)
            {
                if (other.Id == nodeId)
                {
                    continue;
                }

                if (AreRelated(nodeId, other.Id))
                {
                    continue;
                }

                bool otherHasChildren = _graph.Nodes.Any(n => NormalizeNodeId(n.ParentNodeId) == other.Id);
                bool otherExpandedSubMachineVisible = otherHasChildren && !IsCollapsed(other.Id) && AreAncestorsExpanded(other);

                if (IsNodeVisible(other))
                {
                    var otherRect = GetNodeAnchorRectModel(other);
                    if (movingRect.Overlaps(otherRect))
                    {
                        return node.Position;
                    }
                }

                if (otherExpandedSubMachineVisible)
                {
                    var otherContainerRect = GetNodeAnchorRectModel(other);
                    if (movingRect.Overlaps(otherContainerRect))
                    {
                        return node.Position;
                    }
                }
            }

            return desiredPosition;
        }

        private bool IsWithinParentSubMachineBounds(StateNodeData node, Dictionary<string, Vector2> overrides)
        {
            string parentId = NormalizeNodeId(node.ParentNodeId);
            if (string.IsNullOrEmpty(parentId))
            {
                return true;
            }

            var parent = _graph.FindNode(parentId);
            if (parent == null)
            {
                return true;
            }

            bool nodeHasChildren = _graph.Nodes.Any(n => NormalizeNodeId(n.ParentNodeId) == node.Id);
            bool parentHasChildren = _graph.Nodes.Any(n => NormalizeNodeId(n.ParentNodeId) == parent.Id);
            if (!nodeHasChildren || !parentHasChildren || IsCollapsed(parent.Id))
            {
                return true;
            }

            var parentRect = GetNodeAnchorRectModel(parent, overrides);
            var childRect = GetNodeAnchorRectModel(node, overrides);
            return parentRect.Contains(childRect.min) && parentRect.Contains(childRect.max);
        }

        private Vector2 GetNodePosition(StateNodeData node, Dictionary<string, Vector2> overrides = null)
        {
            if (overrides != null && overrides.TryGetValue(node.Id, out var pos))
            {
                return pos;
            }

            return node.Position;
        }

        private bool AreRelated(string aNodeId, string bNodeId)
        {
            return IsAncestorOf(aNodeId, bNodeId) || IsAncestorOf(bNodeId, aNodeId);
        }

        private bool IsAncestorOf(string ancestorNodeId, string descendantNodeId)
        {
            var cursor = _graph.FindNode(descendantNodeId);
            while (cursor != null)
            {
                string parentId = NormalizeNodeId(cursor.ParentNodeId);
                if (string.IsNullOrEmpty(parentId))
                {
                    return false;
                }

                if (parentId == ancestorNodeId)
                {
                    return true;
                }

                cursor = _graph.FindNode(parentId);
            }

            return false;
        }

        private bool AreAncestorsExpanded(StateNodeData node)
        {
            string parentId = NormalizeNodeId(node.ParentNodeId);
            while (!string.IsNullOrEmpty(parentId))
            {
                if (IsCollapsed(parentId))
                {
                    return false;
                }

                var parent = _graph.FindNode(parentId);
                if (parent == null)
                {
                    break;
                }

                parentId = NormalizeNodeId(parent.ParentNodeId);
            }

            return true;
        }

        private string GetEffectiveRootEntryNodeId()
        {
            if (_graph == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(_graph.EntryNodeId))
            {
                var entryNode = _graph.FindNode(_graph.EntryNodeId);
                if (entryNode != null)
                {
                    var ancestor = FindTopLevelAncestor(entryNode);
                    if (ancestor != null)
                    {
                        return ancestor.Id;
                    }
                }
            }

            return _graph.Nodes.FirstOrDefault(n => string.IsNullOrWhiteSpace(n.ParentNodeId))?.Id;
        }

        private StateNodeData FindTopLevelAncestor(StateNodeData node)
        {
            var current = node;
            var visited = new HashSet<string>();
            while (current != null && !string.IsNullOrWhiteSpace(current.ParentNodeId))
            {
                if (!visited.Add(current.Id))
                {
                    break;
                }

                current = _graph.FindNode(current.ParentNodeId);
            }

            return current;
        }

        private bool IsEffectiveInitialOfParent(string nodeId)
        {
            var node = _graph.FindNode(nodeId);
            if (node == null)
            {
                return false;
            }

            string parentId = NormalizeNodeId(node.ParentNodeId);
            if (string.IsNullOrEmpty(parentId))
            {
                return false;
            }

            var parent = _graph.FindNode(parentId);
            if (parent == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(parent.InitialChildNodeId))
            {
                return parent.InitialChildNodeId == nodeId;
            }

            var firstChild = _graph.Nodes.FirstOrDefault(n => NormalizeNodeId(n.ParentNodeId) == parentId);
            return firstChild != null && firstChild.Id == nodeId;
        }

        private bool IsCollapsed(string nodeId)
        {
            return !string.IsNullOrEmpty(nodeId) && _collapsedNodeIds.Contains(nodeId);
        }

        private void CollapseNode(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId))
            {
                return;
            }

            _collapsedNodeIds.Add(nodeId);
            Repaint();
        }

        private void ExpandNode(string nodeId)
        {
            if (string.IsNullOrEmpty(nodeId))
            {
                return;
            }

            _collapsedNodeIds.Remove(nodeId);
            Repaint();
        }

        private static bool TryResolveStateContextType(Type stateType, Type contextType, out Type stateContextType)
        {
            stateContextType = null;

            Type candidateType = stateType;
            if (stateType.IsGenericTypeDefinition)
            {
                var args = stateType.GetGenericArguments();
                if (args.Length != 1)
                {
                    return false;
                }

                try
                {
                    candidateType = stateType.MakeGenericType(contextType);
                }
                catch
                {
                    return false;
                }
            }

            var current = candidateType;
            while (current != null)
            {
                if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(StateBase<>))
                {
                    stateContextType = current.GetGenericArguments()[0];
                    return true;
                }

                current = current.BaseType;
            }

            return false;
        }

        private static string[] BuildStateTypeLabels(List<Type> stateTypes)
        {
            var labels = new string[stateTypes.Count + 1];
            labels[0] = "(Unassigned)";
            for (int i = 0; i < stateTypes.Count; i++)
            {
                labels[i + 1] = stateTypes[i].FullName;
            }

            return labels;
        }

        private void DrawValidation()
        {
            var errors = StateMachineGraphValidator.Validate(_graph);
            if (errors.Count == 0)
            {
                EditorGUILayout.HelpBox("Validation OK", MessageType.Info);
                return;
            }

            EditorGUILayout.HelpBox(string.Join("\n", errors), MessageType.Warning);
        }

        private static void EnsureStateTypeCache()
        {
            if (_cachedStateTypes != null && _cachedStateTypeLabels != null)
            {
                return;
            }

            var typeSet = new HashSet<Type>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t != null).ToArray();
                }
                catch
                {
                    continue;
                }

                foreach (var type in types)
                {
                    if (type == null || type.IsAbstract)
                    {
                        continue;
                    }

                    if (!IsStateType(type))
                    {
                        continue;
                    }

                    typeSet.Add(type);
                }
            }

            _cachedStateTypes = typeSet.ToList();

            _cachedStateTypes = _cachedStateTypes
                .OrderBy(t => t.FullName)
                .ToList();

            _cachedStateTypeLabels = new string[_cachedStateTypes.Count + 1];
            _cachedStateTypeLabels[0] = "(Unassigned)";
            for (int i = 0; i < _cachedStateTypes.Count; i++)
            {
                _cachedStateTypeLabels[i + 1] = _cachedStateTypes[i].FullName;
            }
        }

        private static void EnsureContextTypeCache()
        {
            if (_cachedContextTypes != null && _cachedContextTypeLabels != null)
            {
                return;
            }

            EnsureStateTypeCache();

            var contextSet = new HashSet<Type>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!IsUserCodeAssembly(assembly))
                {
                    continue;
                }

                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    types = ex.Types.Where(t => t != null).ToArray();
                }
                catch
                {
                    continue;
                }

                foreach (var type in types)
                {
                    if (type == null
                        || !type.IsClass
                        || type.IsAbstract
                        || type.ContainsGenericParameters
                        || type.IsNestedPrivate
                        || !type.IsSubclassOf(typeof(StateMachineContext)))
                    {
                        continue;
                    }

                    contextSet.Add(type);
                }
            }

            _cachedContextTypes = contextSet
                .OrderBy(t => t.FullName)
                .ToList();

            _cachedContextTypeLabels = new string[_cachedContextTypes.Count];
            for (int i = 0; i < _cachedContextTypes.Count; i++)
            {
                _cachedContextTypeLabels[i] = _cachedContextTypes[i].FullName;
            }
        }

        private static bool IsUserCodeAssembly(Assembly assembly)
        {
            var name = assembly.GetName().Name ?? string.Empty;
            if (name.StartsWith("System", StringComparison.Ordinal)
                || name.StartsWith("mscorlib", StringComparison.Ordinal)
                || name.StartsWith("netstandard", StringComparison.Ordinal)
                || name.StartsWith("Mono", StringComparison.Ordinal)
                || name.StartsWith("Unity", StringComparison.Ordinal))
            {
                return false;
            }

            if (name.EndsWith("-Editor", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return true;
        }

        private static bool IsStateType(Type type)
        {
            var current = type;
            while (current != null)
            {
                if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(StateBase<>))
                {
                    return true;
                }

                current = current.BaseType;
            }

            return false;
        }

        private void BindGraph(StateMachineGraph graph)
        {
            _graph = graph;
            _controller = _graph != null ? new StateMachineGraphController(_graph) : null;
            _collapsedNodeIds.Clear();
            _selectedNodeId = null;
            _newTransitionTargetIndex = 0;
            _renamingNodeId = null;
            _renameBuffer = null;
            _focusRenameField = false;

            EnsureContextTypeCache();
            if (_graph != null && string.IsNullOrWhiteSpace(_graph.ContextTypeName) && _cachedContextTypes.Count > 0)
            {
                _newGraphContextTypeIndex = Mathf.Clamp(_newGraphContextTypeIndex, 0, _cachedContextTypes.Count - 1);
                _controller.SetGraphContextType(_cachedContextTypes[_newGraphContextTypeIndex].AssemblyQualifiedName);
            }

            if (_graph != null && !string.IsNullOrWhiteSpace(_graph.ContextTypeName))
            {
                int idx = _cachedContextTypes.FindIndex(t => t.AssemblyQualifiedName == _graph.ContextTypeName);
                if (idx >= 0)
                {
                    _newGraphContextTypeIndex = idx;
                }
            }

            Repaint();
        }

        private string GetNodePath(string nodeId)
        {
            string cursor = NormalizeNodeId(nodeId);
            if (string.IsNullOrEmpty(cursor))
            {
                return "Root";
            }

            var labels = new List<string>();
            var visited = new HashSet<string>();
            while (!string.IsNullOrEmpty(cursor))
            {
                if (!visited.Add(cursor))
                {
                    labels.Add("[Cycle]");
                    break;
                }

                var node = _graph.FindNode(cursor);
                if (node == null)
                {
                    labels.Add("(missing)");
                    break;
                }

                labels.Add(node.Name);
                cursor = NormalizeNodeId(node.ParentNodeId);
            }

            labels.Reverse();
            return "Root/" + string.Join("/", labels);
        }

        private int CountOutgoingTransitions(string fromNodeId)
        {
            return _graph.Transitions.Count(t => t.FromNodeId == fromNodeId);
        }

        private int CountIncomingTransitions(string toNodeId)
        {
            return _graph.Transitions.Count(t => t.ToNodeId == toNodeId);
        }

        private HashSet<string> CollectDescendantIds(string nodeId)
        {
            var result = new HashSet<string>();
            var queue = new Queue<string>();
            queue.Enqueue(nodeId);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var child in _graph.Nodes.Where(n => NormalizeNodeId(n.ParentNodeId) == current))
                {
                    if (!result.Add(child.Id))
                    {
                        continue;
                    }

                    queue.Enqueue(child.Id);
                }
            }

            return result;
        }

        private static string NormalizeNodeId(string nodeId)
        {
            return string.IsNullOrWhiteSpace(nodeId) ? null : nodeId;
        }

        private void StartNodeRename(StateNodeData node)
        {
            _renamingNodeId = node.Id;
            _renameBuffer = node.Name;
            _focusRenameField = true;
            _isNodeDragging = false;
            _dragNodeId = null;
        }

        private void CommitNodeRename()
        {
            if (string.IsNullOrEmpty(_renamingNodeId))
            {
                return;
            }

            _controller.RenameNode(_renamingNodeId, _renameBuffer);
            _renamingNodeId = null;
            _renameBuffer = null;
            _focusRenameField = false;
            Repaint();
        }

        private void CancelNodeRename()
        {
            _renamingNodeId = null;
            _renameBuffer = null;
            _focusRenameField = false;
            Repaint();
        }

        private void CreateGraphAsset()
        {
            EnsureContextTypeCache();

            var path = EditorUtility.SaveFilePanelInProject(
                "Create State Machine Graph",
                "StateMachineGraph",
                "asset",
                "Choose where to save the StateMachineGraph asset.");

            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            var graph = CreateInstance<StateMachineGraph>();
            if (_cachedContextTypes.Count > 0)
            {
                _newGraphContextTypeIndex = Mathf.Clamp(_newGraphContextTypeIndex, 0, _cachedContextTypes.Count - 1);
                graph.ContextTypeName = _cachedContextTypes[_newGraphContextTypeIndex].AssemblyQualifiedName;
            }

            AssetDatabase.CreateAsset(graph, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = graph;
            BindGraph(graph);
        }
    }
}
