using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;
using KModkit;

public class AreaSplitScript : MonoBehaviour
{
    public KMNeedyModule Needy;
    public KMBombInfo BombInfo;
    public KMAudio Audio;

    public GameObject GridParent;
    public GameObject[] CubeObjs;
    public Material[] CubeMats;
    public KMSelectable[] CubeSels;

    private int _moduleId;
    private static int _moduleIdCounter = 1;

    private static readonly string[] _colorNames = new string[4] { "Red", "Yellow", "Green", "Blue" };
    private const int width = 8;
    private const int height = 5;
    private int[] _grid = new int[width * height];
    private int _correctColor;
    private int? _currentlySelectedColor;

    private void Start()
    {
        _moduleId = _moduleIdCounter++;
        Needy.OnNeedyActivation += NeedyActivate;
        Needy.OnNeedyDeactivation += NeedyDeactivate;
        Needy.OnTimerExpired += TimerExpire;
        for (int i = 0; i < CubeSels.Length; i++)
            CubeSels[i].OnInteract += CubePress(i);
        StartCoroutine(Init());
    }

    private KMSelectable.OnInteractHandler CubePress(int btn)
    {
        return delegate ()
        {
            Audio.PlaySoundAtTransform("click", CubeSels[btn].transform);
            CubeSels[btn].AddInteractionPunch(0.2f);
            _currentlySelectedColor = _grid[btn];
            for (int i = 0; i < _grid.Length; i++)
                CubeObjs[i].GetComponent<MeshRenderer>().material = CubeMats[_grid[i] + (_grid[i] == _grid[btn] ? 4 : 0)];
            return false;
        };
    }

    private IEnumerator Init()
    {
        yield return null;
        GridParent.SetActive(false);
    }

    private void TimerExpire()
    {
        if (_currentlySelectedColor == null)
        {
            Needy.HandleStrike();
            Debug.LogFormat("[Area Split #{0}] No color was selected. Strike.", _moduleId);
        }
        else if (_currentlySelectedColor == _correctColor)
            Debug.LogFormat("[Area Split #{0}] {1} was correctly selected. Needy disarmed.", _moduleId, _colorNames[_currentlySelectedColor.Value]);
        else
        {
            Needy.HandleStrike();
            Debug.LogFormat("[Area Split #{0}] {1} was incorrectly selected. Strike.", _moduleId, _colorNames[_currentlySelectedColor.Value]);
        }
        NeedyDeactivate();
    }

    private void NeedyActivate()
    {
        _currentlySelectedColor = null;
        Needy.SetResetDelayTime(30f, 50f);
        _grid = GenerateGrid();
        for (int i = 0; i < width * height; i++)
            CubeObjs[i].GetComponent<MeshRenderer>().material = CubeMats[_grid[i]];
        GridParent.SetActive(true);
        var gridString = _grid.Select(i => "RYGB"[i]).Join("");
        Debug.LogFormat("[Area Split #{0}] Grid:", _moduleId);
        for (int r = 0; r < 5; r++)
            Debug.LogFormat("[Area Split #{0}] {1}", _moduleId, Enumerable.Range(r * 8, 8).Select(i => gridString[i]).Join(""));
        _correctColor = GetMostCommonOccurrence(_grid);
        Debug.LogFormat("[Area Split #{0}] Most common color is {1}.", _moduleId, _colorNames[_correctColor]);
    }

    private void NeedyDeactivate()
    {
        Needy.HandlePass();
        GridParent.SetActive(false);
    }

    private int[] GenerateGrid()
    {
        tryAgain:
        var tempGrid = new int?[width * height];
        var randomStarts = Enumerable.Range(0, _grid.Length)
            .Where(i => i % width == 0 || i % width == width - 1 || i / width == 0 || i / width == height - 1)
            .ToArray().Shuffle().Take(4).ToArray();
        for (int i = 0; i < randomStarts.Length; i++)
            tempGrid[randomStarts[i]] = i % 4;
        var a = randomStarts.Contains(1);
        while (tempGrid.Any(i => i == null))
        {
            var rndIx = Enumerable.Range(0, tempGrid.Length).Where(i => tempGrid[i] != null).PickRandom();
            var adjs = GetAdjacents(rndIx).Where(i => tempGrid[i] == null).ToArray();
            if (adjs.Length == 0)
                continue;
            tempGrid[adjs.PickRandom()] = tempGrid[rndIx].Value;
        }
        if (Enumerable.Range(0, 4).Select(i => tempGrid.Where(x => x == i).Count()).Any(i => i < 7) || Enumerable.Range(0, 4).Select(i => tempGrid.Where(x => x == i).Count()).Any(i => i > 14) || FindClumps(tempGrid) != 4 || Enumerable.Range(0, 4).Select(i => tempGrid.Where(x => x == i).Count()).Distinct().Count() != 4)
            goto tryAgain;
        return tempGrid.Select(i => (int)i).ToArray();
    }

    private int GetMostCommonOccurrence(int[] grid)
    {
        var arr = new int[4];
        for (int i = 0; i < grid.Length; i++)
            arr[grid[i]]++;
        return Array.IndexOf(arr, arr.Max());
    }

    private int FindClumps(int?[] grid)
    {
        List<List<int>> clumps = new List<List<int>>();
        for (int i = 0; i < grid.Length; i++)
        {
            var candidateClumps = GetAdjacents(i)
                .Where(adj => grid[i] == grid[adj])
                .Select(adj => clumps.FirstOrDefault(clump => clump.Contains(adj)))
                .Where(clump => clump != null);
            var newClump = new List<int>();
            foreach (var clump in candidateClumps)
            {
                clumps.Remove(clump);
                newClump.AddRange(clump);
            }
            newClump.Add(i);
            clumps.Add(newClump);
        }
        return clumps.Count();
    }

    private IEnumerable<int> GetAdjacents(int num)
    {
        if (num % width > 0)
            yield return num - 1;
        if (num % width < width - 1)
            yield return num + 1;
        if (num / width > 0)
            yield return num - width;
        if (num / width < height - 1)
            yield return num + width;
    }

#pragma warning disable 0414
    private readonly string TwitchHelpMessage = "!{0} press red/yellow/green/blue [Press that color.] | 'press' is optional.";
#pragma warning restore 0414

    private IEnumerator ProcessTwitchCommand(string command)
    {
        var m = Regex.Match(command, @"^\s*(press\s+)?((?<red>red)|(?<yellow>yellow)|(?<green>green)|(?<blue>blue))\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!m.Success)
            yield break;
        yield return null;
        yield return "strike";
        yield return "solve";
        if (m.Groups["red"].Success)
            CubeSels[Array.IndexOf(_grid, 0)].OnInteract();
        else if (m.Groups["yellow"].Success)
            CubeSels[Array.IndexOf(_grid, 1)].OnInteract();
        else if (m.Groups["green"].Success)
            CubeSels[Array.IndexOf(_grid, 2)].OnInteract();
        else if (m.Groups["blue"].Success)
            CubeSels[Array.IndexOf(_grid, 3)].OnInteract();
    }

    private void TwitchHandleForcedSolve()
    {
        StartCoroutine(Autosolve());
    }

    private IEnumerator Autosolve()
    {
        while (true)
        {
            while (_currentlySelectedColor != null || !GridParent.activeInHierarchy)
                yield return true;
            CubeSels[Array.IndexOf(_grid, _correctColor)].OnInteract();
            yield return true;
        }
    }
}
