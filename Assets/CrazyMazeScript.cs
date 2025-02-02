﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CrazyMaze;
using UnityEngine;

using Rnd = UnityEngine.Random;

public class CrazyMazeScript : MonoBehaviour
{
    public KMBombModule Module;
    public KMBombInfo Bomb;
    public KMAudio Audio;
    public KMSelectable[] Arrows;
    public TextMesh[] ArrowTexts;   // for TP only
    public KMSelectable Bridge;
    public TextMesh CurCellText, GoalCellText;
    public SpriteRenderer CurrentCell;
    public Sprite[] Sprites;
    public MeshRenderer PuzzleBG;
    public Material PuzzleOffMat;
    public KMRuleSeedable RuleSeedable;

    private static int _moduleIdCounter = 1;
    private int _moduleID = 0;
    private bool _moduleSolved = false;

    private Coroutine _textAnimCoroutine;
    private int _currentCell, _goalCell, _startingCell;
    private bool _showingGoal;

    private HashSet<int>[] _passable;
    private string[] _cellLetters;

    void Awake()
    {
        _moduleID = _moduleIdCounter++;
        for (int i = 0; i < Arrows.Length; i++)
            Arrows[i].OnInteract += ArrowPress(i, Arrows[i]);
        Bridge.OnInteract += ArrowPress(-1, Bridge);
        _passable = Enumerable.Range(0, 26 * 26).Select(ix => new HashSet<int>()).ToArray();
    }

    private KMSelectable.OnInteractHandler ArrowPress(int pos, KMSelectable sel)
    {
        var isBridge = pos == -1;
        var upper = isBridge ? "Traversing bridge" : "Going";
        var lower = isBridge ? "traverse bridge" : "go";

        return delegate
        {
            StartCoroutine(MoveAnim());
            Audio.PlaySoundAtTransform("press", sel.transform);
            sel.AddInteractionPunch(.7f);
            if (_moduleSolved)
                return false;

            var goingTo = isBridge
                ? CellTransitions.All[_currentCell].BridgeDestination
                : CellTransitions.All[_currentCell].Neighbors[pos].ToCell;

            if (goingTo != null && _passable[_currentCell].Contains(goingTo.Value))
            {
                Debug.LogFormat(@"[Crazy Maze #{0}] {3} from {1} to {2}.", _moduleID, _cellLetters[_currentCell], _cellLetters[goingTo.Value], upper);
                if (isBridge)
                {
                    _showingGoal = !_showingGoal;
                    Audio.PlaySoundAtTransform("bridge", sel.transform);
                    CurCellText.transform.localPosition = new Vector3(CurCellText.transform.localPosition.x, _showingGoal ? 0.025f : 0.03f, CurCellText.transform.localPosition.z);
                    GoalCellText.transform.localPosition = new Vector3(CurCellText.transform.localPosition.x, _showingGoal ? 0.03f : 0.025f, CurCellText.transform.localPosition.z);
                }
                SetCell(goingTo.Value);
                if (goingTo.Value == _goalCell)
                {
                    Debug.LogFormat(@"[Crazy Maze #{0}] Module solved.", _moduleID);
                    Module.HandlePass();
                    _moduleSolved = true;
                    foreach (KMSelectable arrow in Arrows)
                        arrow.gameObject.SetActive(false);
                    Bridge.gameObject.SetActive(false);
                    StartCoroutine(SolveAnim());
                    Audio.PlaySoundAtTransform("solve", CurrentCell.transform);
                    if (_textAnimCoroutine != null)
                        StopCoroutine(_textAnimCoroutine);
                    CurCellText.transform.localPosition = new Vector3(0, 0.0151f, 0.0044f);
                    CurCellText.color = new Color(1, 1, 1, 100f / 255);
                    CurCellText.characterSize = .00125f;
                    CurCellText.text = "NICE";
                    GoalCellText.gameObject.SetActive(false);
                }
            }
            else if (goingTo != null)
            {
                Debug.LogFormat(@"[Crazy Maze #{0}] Attempt to {3} from {1} to {2}. Strike.", _moduleID, _cellLetters[_currentCell], _cellLetters[goingTo.Value], lower);
                Module.HandleStrike();
            }
            else
            {
                Debug.LogFormat(@"[Crazy Maze #{0}] Attempt to cross a non-existent bridge from {1}. Strike.", _moduleID, _cellLetters[_currentCell]);
                Module.HandleStrike();
            }

            return false;
        };
    }

    void Start()
    {
        // ## RULE SEED
        var rnd = RuleSeedable.GetRNG();
        Debug.LogFormat(@"[Crazy Maze #{0}] Using rule seed: {1}.", _moduleID, rnd.Seed);
        var cells = Enumerable.Range(0, 676).ToList();
        var links = Enumerable.Range(0, 676)
            .Select(cellIx => CellTransitions.All[cellIx])
            .Select(tr => new { tr.BridgeDestination, Neighbors = tr.Neighbors.Where(n => n.ToCell != null).Select(n => n.ToCell.Value) })
            .Select(tr => tr.BridgeDestination == null ? tr.Neighbors : tr.Neighbors.Concat(new[] { tr.BridgeDestination.Value }))
            .Select(cs => cs.OrderBy(c => c).ToArray())
            .ToArray();

        // Find a random starting cell
        var startCellIx = rnd.Next(0, cells.Count);
        var startCell = cells[startCellIx];

        // Maze algorithm starts here
        var todo = new List<int> { startCell };
        cells.RemoveAt(startCellIx);

        while (cells.Count > 0)
        {
            var ix = rnd.Next(0, todo.Count);
            var cell = todo[ix];

            var availableLinks = links[cell].Where(otherCell => cells.Contains(otherCell)).ToArray();
            if (availableLinks.Length == 0)
                todo.RemoveAt(ix);
            else
            {
                var otherCell = availableLinks[availableLinks.Length == 1 ? 0 : rnd.Next(0, availableLinks.Length)];
                _passable[cell].Add(otherCell);
                _passable[otherCell].Add(cell);
                cells.Remove(otherCell);
                todo.Add(otherCell);
            }
        }

        var letters = Enumerable.Range(0, 26).Select(c => (char) ('A' + c));
        _cellLetters = rnd.ShuffleFisherYates(letters.SelectMany(ltr => letters.Select(ltr2 => ltr + "" + ltr2)).ToArray());
        // End rule seed

        // Decide on a start cell
        _startingCell = Rnd.Range(0, 26 * 26);
        //_startingCell = _cellLetters.IndexOf(c => c == "XS");
        SetCell(_startingCell);

        // Decide on a goal cell that is a certain distance away
        const int minAllowedDistance = 12;
        const int maxAllowedDistance = 20;
        var visited = new HashSet<int> { _currentCell };
        var chooseFrom = new HashSet<int>();
        var curDist = 0;
        while (curDist <= maxAllowedDistance)
        {
            var newCells = visited.SelectMany(cel => _passable[cel]).Except(visited).ToArray();
            if (curDist >= minAllowedDistance)
                chooseFrom.UnionWith(newCells);
            visited.UnionWith(newCells);
            curDist++;
        }
        _goalCell = chooseFrom.PickRandom();

        Debug.LogFormat(@"[Crazy Maze #{0}] Start cell: {1}", _moduleID, _cellLetters[_currentCell]);
        Debug.LogFormat(@"[Crazy Maze #{0}] Goal cell: {1}", _moduleID, _cellLetters[_goalCell]);

        _textAnimCoroutine = StartCoroutine(TextAnim(CurCellText.transform, 0, adjustX: -.02f, adjustZ: .02f));
        StartCoroutine(TextAnim(GoalCellText.transform, 1, adjustX: .02f, adjustZ: -.02f));
        Module.OnActivate += Activate;
    }

    private void Activate()
    {
        foreach (var arTx in ArrowTexts)
            arTx.gameObject.SetActive(TwitchPlaysActive);
    }

    void SetCell(int cell)
    {
        var cellInfo = CellTransitions.All[cell];
        var transitions = cellInfo.Neighbors;

        for (int arIx = 0; arIx < Arrows.Length; arIx++)
        {
            if (arIx >= transitions.Length)
                Arrows[arIx].gameObject.SetActive(false);
            else
            {
                Arrows[arIx].transform.localPosition = new Vector3(transitions[arIx].ArrowX, .0154f, -transitions[arIx].ArrowY);
                Arrows[arIx].transform.localEulerAngles = new Vector3(0, transitions[arIx].ArrowAngle, 0);
                Arrows[arIx].gameObject.SetActive(true);
            }
        }
        _currentCell = cell;
        CurrentCell.sprite = Sprites[cellInfo.SpriteIx];
    }

    private IEnumerator SolveAnim(float fadeDuration = 2f)
    {
        PuzzleBG.material = PuzzleOffMat;
        PuzzleBG.material.color = Color.white;
        yield return new WaitForSeconds(1f / 30);
        PuzzleBG.material.color = Color.black;
        CurrentCell.color = Color.white;
        CurrentCell.transform.localScale = Vector3.one * 0.04f;
        float timer = 0;
        while (timer < fadeDuration)
        {
            yield return null;
            timer += Time.deltaTime;
            CurrentCell.color = Color.Lerp(Color.white, new Color(1, 1, 1, 0), timer / fadeDuration);
            CurrentCell.transform.localScale = Vector3.one * 0.04f * Easing.OutExpo(timer, 1, 0, fadeDuration);
        }
        CurrentCell.gameObject.SetActive(false);
    }

    private IEnumerator MoveAnim()
    {
        CurrentCell.color = Color.white;
        yield return new WaitForSeconds(1f / 30);
        CurrentCell.color = new Color(1, 1, 1, 0.5f);
    }

    private IEnumerator TextAnim(Transform trf, float offset, float duration = 2f, float intensity = 0.018f, float adjustX = 0.02f, float adjustZ = 0.02f)
    {
        while (!_moduleSolved)
        {
            var timer = Time.time;
            float x = Mathf.Cos((timer + offset) / duration * 2 * Mathf.PI) * intensity;
            float z = Mathf.Sin((timer + offset) / duration * 2 * Mathf.PI) * intensity;
            trf.localPosition = new Vector3(x + adjustX, trf.localPosition.y, z + adjustZ);
            CurCellText.text = _showingGoal ? "??" : _cellLetters[_currentCell];
            GoalCellText.text = _showingGoal ? _cellLetters[_goalCell] : "??";
            yield return null;
        }
    }

#pragma warning disable 414
#pragma warning disable 649
    private readonly string TwitchHelpMessage = @"!{0} move 123 [bridge] (The first number in each command identifies a numbered arrow. All subsequent numbers within the same command select an edge counting clockwise from the edge that was traversed last. For example: Traversing to a 5 sided shape, u-turning and counting the traversed side as 0, 4 will traverse you to your immediate left. | !{0} bridge | !{0} reset (return to starting location)";
    private bool TwitchPlaysActive;
#pragma warning restore 649
#pragma warning restore 414

    private IEnumerator ProcessTwitchCommand(string command)
    {
        string[] parameters = command.Split(' ');

        for (int i = 0; i < parameters.Length; i++)
        {
            yield return null;
            Match match = Regex.Match(parameters[i], @"^(bridge|reset|move)$", RegexOptions.IgnoreCase);

            if (!match.Success)
            {
                yield return $"sendtochaterror @{0}, unrecognized command: " + parameters[i];
                yield break;
            }

            switch (match.Groups[1].Value.ToLower())
            {
                case "reset":
                    SetCell(_startingCell);
                    _showingGoal = false;
                    break;

                case "bridge":
                    yield return new[] { Bridge };
                    break;

                case "move":
                    if (i + 1 >= parameters.Length)
                    {
                        yield return "sendtochaterror @{0}, no numbers provided after 'move'.";
                        yield break;
                    }

                    var vals = parameters[i + 1].Where(ch => ch >= '1' && ch <= '8').ToArray();

                    if (vals.Length == 0)
                    {
                        yield return "sendtochaterror @{0}, those are not valid numbers.";
                        yield break;
                    }

                    var prevVal = 0;
                    for (var j = 0; j < vals.Length; j++)
                    {
                        yield return null;
                        var val = vals[j] - '1';
                        var numArrows = Arrows.Count(a => a.gameObject.activeSelf);
                        if (val >= numArrows)
                        {
                            yield return j == 0
                                ? "sendtochaterror @{0}, that first number is not a valid arrow."
                                : j == 1
                                    ? "sendtochaterror @{0}, I executed the first move but the second one is greater than the number of arrows."
                                    : string.Format("sendtochaterror @{{0}}, I executed the first {0} of your moves but the next one is greater than the number of arrows.", j);
                            yield break;
                        }
                        if (j > 0)
                            val = (val + prevVal + 1) % numArrows;
                        var prevCell = _currentCell;
                        yield return new[] { Arrows[val] };
                        prevVal = CellTransitions.All[_currentCell].Neighbors.IndexOf(n => n.ToCell == prevCell);
                    }
                    i++;
                    break;
            }
        }
    }

    public IEnumerator TwitchHandleForcedSolve()
    {
        if (_moduleSolved)
            yield break;

        var q = new Queue<int>();
        q.Enqueue(_currentCell);
        var parents = new Dictionary<int, int>();
        parents[_currentCell] = -1;
        while (q.Count > 0)
        {
            var cell = q.Dequeue();
            if (cell == _goalCell)
                break;

            var neighbors = CellTransitions.All[cell].Neighbors;
            for (var dir = 0; dir < neighbors.Length; dir++)
                if (neighbors[dir].ToCell != null)
                {
                    var newCell = neighbors[dir].ToCell.Value;
                    if (!_passable[cell].Contains(newCell) || parents.ContainsKey(newCell))
                        continue;
                    parents[newCell] = cell;
                    q.Enqueue(newCell);
                }
            if (CellTransitions.All[cell].BridgeDestination != null)
            {
                var newCell = CellTransitions.All[cell].BridgeDestination.Value;
                if (!_passable[cell].Contains(newCell) || parents.ContainsKey(newCell))
                    continue;
                parents[newCell] = cell;
                q.Enqueue(newCell);
            }
        }

        var path = new List<int>();
        var c = _goalCell;
        while (c != _currentCell)
        {
            path.Add(c);
            c = parents[c];
        }
        for (int i = path.Count - 1; i >= 0; i--)
        {
            var buttonToPress = CellTransitions.All[_currentCell].Neighbors.IndexOf(n => n.ToCell == path[i]);
            (buttonToPress == -1 ? Bridge : Arrows[buttonToPress]).OnInteract();
            yield return new WaitForSeconds(.1f);
        }
    }
}