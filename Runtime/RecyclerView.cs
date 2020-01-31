using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Jobs;
using Unity.Burst;
using Unity.Mathematics;
using Unity.Jobs;

namespace Digitopolis.RecyclerView {
    public enum ScrollDirection {
        None = 0,
        Up,
        Down,
    }

    public enum CellChange {
        Add = 0,
        Remove,
    }

    internal class Cell {
        private int itemIndex;
        internal GameObject gameObject;

        public Cell(int index, GameObject cell) {
            this.itemIndex = index;
            this.gameObject = cell;
            this.gameObject.name = index.ToString();
        }

        public int ItemIndex {
            get {
                return itemIndex;
            }
            set {
                itemIndex = value;
                gameObject.name = itemIndex.ToString();
            }
        }
    }

    [BurstCompile]
    internal struct WarpToTargetJob : IJobParallelForTransform {
        [ReadOnly]
        public NativeArray<Vector3> targets;

        public void Execute(int index, TransformAccess transformAccess) {
            var target = targets[index];
            transformAccess.localPosition = new Vector3(target.x, target.y, target.z);
        }
    }

    public class RecyclerView : MonoBehaviour {
#pragma warning disable 649
        [Header("RectTransform")]
        [SerializeField] private RectTransform m_ScrollViewRectTransform;

        [SerializeField] private RectTransform m_Viewport;
        [SerializeField] private RectTransform m_Content;
        [SerializeField] private GameObject m_CellPrefab;

        [Header("Config")]
        [Tooltip("It will need group when you want to add/remove cell")]
        [SerializeField] private bool m_IsNeedToCreateGroup = true;
#pragma warning restore 649

        private int m_TempCurrentIndex;
        private int m_CurrentIndex;
        private int m_LastShowedIndex;

        private int m_NumberOfItem;
        private int m_NumberOfItemInViewport;
        private float m_ViewportItemHeight;
        private float m_CellItemHeight;
        private float m_Spacing;
        private List<Cell> m_Cells;

        private ScrollDirection m_ScrollDirection;

        private Action<int, GameObject> m_OnRender;
        private Action<GameObject> m_OnClear;

        private List<GameObject> m_Parents;

        public void Init(int numberOfItem, float cellItemHeight, float spacing, Action<int, GameObject> onRender,
            Action<GameObject> onClear) {
            if (m_Parents != null && m_Parents.Count != 0) {
                foreach (var parent in m_Parents) {
                    Destroy(parent);
                }
            }

            m_Cells = new List<Cell>(numberOfItem);
            m_Parents = new List<GameObject>();

            m_CellItemHeight = cellItemHeight;
            m_ViewportItemHeight = m_ScrollViewRectTransform.sizeDelta.y + m_Viewport.sizeDelta.y;
            m_Spacing = spacing;
            m_NumberOfItem = numberOfItem;
            m_NumberOfItemInViewport = CalculateNumberOfItemInViewport();
            m_Content.sizeDelta = new Vector2(m_Content.sizeDelta.x, CalculateContentSize());
            m_Content.localPosition = Vector3.zero;

            m_CurrentIndex = 0;
            m_LastShowedIndex = m_NumberOfItemInViewport - 1;

            m_OnRender = onRender;
            m_OnClear = onClear;

            GenerateStarterCell();
        }

        public void OnValueChanged() {
            m_TempCurrentIndex = m_CurrentIndex;

            m_CurrentIndex = CalculateCurrentIndex();
            m_LastShowedIndex = CalculateLastShowedIndex();

            m_ScrollDirection = ScrollDirection.None;

            if (m_CurrentIndex != m_TempCurrentIndex) {
                m_ScrollDirection = m_CurrentIndex > m_TempCurrentIndex ? ScrollDirection.Down : ScrollDirection.Up;
            }


            switch (m_ScrollDirection) {
                case ScrollDirection.None:
                    break;
                case ScrollDirection.Down:
                    for (var i = m_TempCurrentIndex; i <= m_CurrentIndex; i++) {
                        m_TempCurrentIndex = i;
                        ReuseIfNeed(i, m_ScrollDirection);
                    }
                    break;
                case ScrollDirection.Up:
                    for (var i = m_TempCurrentIndex; i >= m_CurrentIndex; i--) {
                        m_TempCurrentIndex = i;
                        ReuseIfNeed(i, m_ScrollDirection);
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void GenerateStarterCell() {
            var countGenerate = m_NumberOfItem < m_NumberOfItemInViewport
                ? m_NumberOfItem
                : m_NumberOfItemInViewport + 4;

            if (m_IsNeedToCreateGroup) {
                CreateCellsWithGroup(countGenerate);
                return;
            }

            CreateCellsWithoutGroup(countGenerate);
        }

        private void CreateCellsWithGroup(int countGenerate) {
            if (m_IsNeedToCreateGroup) {
                GameObject CreateParentGroup(string groupName) {
                    var go = new GameObject($"Group {groupName}", typeof(CanvasRenderer), typeof(Image));
                    var goRectTransform = go.GetComponent<RectTransform>();
                    goRectTransform.anchorMin = new Vector2(0.5f, 1f);
                    goRectTransform.anchorMax = new Vector2(0.5f, 1f);
                    goRectTransform.pivot = new Vector2(0.5f, 0.5f);
                    goRectTransform.sizeDelta = new Vector2(0f, 0f);

                    var goTransform = go.transform;

                    goTransform.SetParent(m_Content.transform);
                    goRectTransform.localPosition = Vector3.zero;
                    goRectTransform.anchoredPosition = Vector2.zero;

                    return go;
                }

                var groupNumber = 1;
                var parent = CreateParentGroup($"{groupNumber}");
                m_Parents.Add(parent);

                var parentTransform = parent.transform;


                for (var i = 0; i < countGenerate; i++) {
                    if (i % 3 == 0 && i != 0) {
                        groupNumber++;

                        parent = CreateParentGroup($"{groupNumber}");
                        m_Parents.Add(parent);

                        parentTransform = parent.transform;
                    }
                    CreateCell(i, parentTransform);
                }
            }
        }

        private void CreateCellsWithoutGroup(int countGenerate) {
            for (var i = 0; i < countGenerate; i++) {
                CreateCell(i, m_Content);
            }
        }

        private void ReuseIfNeed(int currentIndex, ScrollDirection scrollDirection) {
            m_LastShowedIndex = CalculateLastShowedIndex();
            var topItem = m_Cells[0];
            var topItemTransform = topItem.gameObject.transform;
            var topItemLocalPosition = topItemTransform.localPosition;
            var topItemNameIndex = topItem.ItemIndex;

            var bottomIndex = m_Cells.Count - 1;
            var bottomItem = m_Cells[bottomIndex];
            var bottomItemTransform = bottomItem.gameObject.transform;
            var bottomItemLocalPosition = m_Cells[bottomIndex].gameObject.transform.localPosition;
            var bottomItemNameIndex = m_Cells[bottomIndex].ItemIndex;

            if (m_CurrentIndex == m_NumberOfItem - m_NumberOfItemInViewport) {
                return;
            }


            switch (scrollDirection) {
                case ScrollDirection.None:
                    break;
                case ScrollDirection.Down:
                    if (bottomItemNameIndex == m_NumberOfItem - 1) {
                        // no need to reuse (ScrollDirection.Down)
                        return;
                    }

                    if (topItemNameIndex <= currentIndex - 2) {
                        topItemTransform.localPosition = new Vector3(
                            topItemLocalPosition.x,
                            bottomItemLocalPosition.y - m_Spacing - m_CellItemHeight,
                            topItemLocalPosition.z
                        );

                        topItem.ItemIndex = bottomItemNameIndex + 1;

                        SwapCellFromIndexToBottom(0);

                        Render(topItem);
                    }
                    break;
                case ScrollDirection.Up:
                    if (topItemNameIndex == 0) {
                        // no need to reuse (ScrollDirection.Up)
                        return;
                    }

                    if (bottomItemNameIndex >= m_LastShowedIndex + 2) {
                        bottomItemTransform.localPosition = new Vector3(
                            bottomItemLocalPosition.x,
                            topItemLocalPosition.y + m_Spacing + m_CellItemHeight,
                            bottomItemLocalPosition.z
                        );

                        bottomItem.ItemIndex = topItemNameIndex - 1;

                        SwapCellFromIndexToTop(bottomIndex);

                        Render(bottomItem);
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(scrollDirection), scrollDirection, null);
            }
        }

        private void Render(Cell cell) {
            if (cell.ItemIndex > m_NumberOfItem - 1 || cell.ItemIndex < 0) {
                cell.gameObject.SetActive(false);
                return;
            }

            m_OnClear(cell.gameObject);

            cell.gameObject.SetActive(true);

            m_OnRender(cell.ItemIndex, cell.gameObject);
        }

        private void Reset() {
            m_Content.localPosition = Vector3.zero;
            OnValueChanged();
        }

        private void CreateCell(int index, Transform parent) {
            var go = Instantiate(m_CellPrefab, parent);
            var goTransform = go.transform;
            var goLocalPosition = go.transform.localPosition;

            goTransform.localScale = Vector3.one;
            goTransform.localPosition = new Vector3(
                goLocalPosition.x,
                -index * (m_CellItemHeight + m_Spacing),
                goLocalPosition.z
            );

            Cell cell = new Cell(index, go);
            Render(cell);

            m_Cells.Add(cell);
        }

        public void AddCellAtTop() {
            Cell lastItem;

            int lastIndex = m_Cells.Count - 1;
            lastItem = m_Cells[lastIndex];
            m_Cells.RemoveAt(lastIndex);

            lastItem.ItemIndex = 0;
            m_Cells.Insert(0, lastItem);

            lastItem.gameObject.transform.localPosition = new Vector3(
                lastItem.gameObject.transform.localPosition.x,
                -lastItem.ItemIndex * (m_CellItemHeight + m_Spacing),
                lastItem.gameObject.transform.localPosition.z
            );

            m_NumberOfItem++;
            Render(lastItem);

            AdjustPositionStartAt(1, CellChange.Add);
            AdjustContentSize();
        }

        public void RemoveCellAt(int indexData) {
            int index = 0;
            for (int i = 0; i < m_Cells.Count; i++) {
                if (m_Cells[i].ItemIndex == indexData) {
                    index = i;
                    break;
                }
            }

            if (index >= m_Cells.Count) {
                Debug.LogError("index greater than item in cell");
                return;
            }


            Cell removeItem;
            if (m_NumberOfItem <= m_NumberOfItemInViewport) {
                removeItem = m_Cells[index];

                m_Cells.RemoveAt(index);

                bool isLastItemInCells = m_Cells.Count == 0;
                if (!isLastItemInCells) {
                    removeItem.ItemIndex = m_Cells[m_Cells.Count - 1].ItemIndex + 1;
                }

                m_Cells.Add(removeItem);
            }
            else if (indexData >= (m_NumberOfItem - m_NumberOfItemInViewport) && indexData < m_NumberOfItem) {
                // Delete in the last section
                removeItem = m_Cells[index];

                m_Cells.RemoveAt(index);
                removeItem.ItemIndex = m_Cells[0].ItemIndex - 1;
                m_Cells.Insert(0, removeItem);

                removeItem.gameObject.transform.localPosition = new Vector3(
                    removeItem.gameObject.transform.localPosition.x,
                    -removeItem.ItemIndex * (m_CellItemHeight + m_Spacing),
                    removeItem.gameObject.transform.localPosition.z
                );

                index++;
            }
            else {
                // Delete in other section
                removeItem = m_Cells[index];

                m_Cells.RemoveAt(index);

                removeItem.ItemIndex = m_Cells[m_Cells.Count - 1].ItemIndex + 1;
                m_Cells.Add(removeItem);
            }

            m_NumberOfItem--;
            Render(removeItem);

            AdjustPositionStartAt(index, CellChange.Remove);
            AdjustContentSize();
        }

        private void AdjustPositionStartAt(int startIndex, CellChange cellChange) {
            var numberOfTargetCells = m_Cells.Count - startIndex;
            var targets = new NativeArray<Vector3>(numberOfTargetCells, Allocator.TempJob);
            var transforms = new Transform[numberOfTargetCells];

            for (var i = startIndex; i < m_Cells.Count; i++) {
                var cell = m_Cells[i];
                var cellTransform = cell.gameObject.transform;
                var cellPosition = cellTransform.localPosition;

                if (cellChange == CellChange.Add) {
                    cell.ItemIndex++;
                }
                else {
                    cell.ItemIndex--;
                }


                transforms[i - startIndex] = cellTransform;

                targets[i - startIndex] = new Vector3(
                    cellPosition.x,
                    -cell.ItemIndex * (m_CellItemHeight + m_Spacing),
                    cellPosition.z
                );
            }

            var warpJob = new WarpToTargetJob {
                targets = targets
            };

            var transformAccessArray = new TransformAccessArray(transforms);
            JobHandle handle = warpJob.Schedule(transformAccessArray);

            handle.Complete();

            transformAccessArray.Dispose();
            targets.Dispose();
        }

        private void AdjustContentSize() {
            m_Content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, CalculateContentSize());
        }

        private void SwapCellFromIndexToTop(int oldIndex) {
            var tempCell = m_Cells[oldIndex];

            m_Cells.RemoveAt(oldIndex);
            m_Cells.Insert(0, tempCell);
        }

        private void SwapCellFromIndexToBottom(int oldIndex) {
            var tempCell = m_Cells[oldIndex];

            m_Cells.RemoveAt(oldIndex);
            m_Cells.Add(tempCell);
        }

        private int CalculateCurrentIndex() =>
            math.clamp(
                (int)math.floor(m_Content.localPosition.y / (m_CellItemHeight + m_Spacing)),
                0,
                (int)m_NumberOfItem - m_NumberOfItemInViewport
            );

        private int CalculateLastShowedIndex() => m_CurrentIndex + m_NumberOfItemInViewport;

        private int CalculateNumberOfItemInViewport() => (int)math.ceil(
            m_ViewportItemHeight / (m_CellItemHeight + m_Spacing));

        private float CalculateContentSize() => m_NumberOfItem * (m_CellItemHeight + m_Spacing);
    }
}
