using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using KModkit;
using rnd = UnityEngine.Random;

public class exoplanets : MonoBehaviour
{
    public new KMAudio audio;
    private KMAudio.KMAudioRef ambianceRef;
    public KMBombInfo bomb;
    public KMBombModule module;

    public GameObject statusLight;
    public KMSelectable[] planetButtons;
    public KMSelectable starButton;
    public Transform[] pivots;
    public Renderer[] planets;
    public Renderer star;
    public Renderer background;
    public Texture[] surfaces;
    public Renderer dummyStar;
    public Renderer[] dummyPlanets;
    public Color solveColor;

    private static readonly float[] sizes = new[] { .015f, .025f, .035f };
    private static readonly float[] speeds = new[] { 5f, 10f, 20f, 40f };
    private int[] planetSizes = new int[3];
    private float[] planetSpeeds = new float[3];
    private int[] planetSurfaces = new int[3];
    private bool[] planetsCcw = new bool[3];
    private bool[] spinningCcw = new bool[3];

    private bool starCcw;
    private int batteryOffset;
    private int targetPlanet;
    private int targetDigit;
    private int startingTargetPlanet;
    private int startingTargetDigit;
    private int tablePosition;
    private int tableRing;
    private static readonly string[][] table = new string[][] {
        new[] { "A", "B", "C", "D", "E", "F", "G", "H" },
        new[] { "I", "J", "K", "L", "M", "N", "O", "P" },
        new[] { "Q", "R", "S", "T", "U", "V", "W", "X" }
    };

    private static readonly string[] positionNames = new[] { "inner", "middle", "outer" };
    private static readonly string[] sizeNames = new[] { "small", "medium", "large" };
    private Vector3[] savedPositions = new Vector3[3];
    private Coroutine starSpinning;
    private Coroutine[] orbits = new Coroutine[3];
    private Coroutine[] tilts = new Coroutine[3];
    private bool planetsHidden;
    private bool isMoving;

    private static int moduleIdCounter = 1;
    private int moduleId;
    private bool moduleSolved;

    #region ModSettings
    exoplanetsSettings settings = new exoplanetsSettings();
#pragma warning disable 414
    private static Dictionary<string, object>[] TweaksEditorSettings = new Dictionary<string, object>[]
    {
      new Dictionary<string, object>
      {
        { "Filename", "Exoplanets Settings.json"},
        { "Name", "Exoplanets" },
        { "Listings", new List<Dictionary<string, object>>
        {
          new Dictionary<string, object>
          {
            { "Key", "PlayAmbiance" },
            { "Text", "Play the space-y ambiance?"}
          }
        }}
      }
    };
#pragma warning restore 414

    private class exoplanetsSettings
    {
        public bool playAmbiance = true;
    }
    #endregion

    private void Awake()
    {
        moduleId = moduleIdCounter++;
        var modConfig = new modConfig<exoplanetsSettings>("Exoplanets Settings");
        settings = modConfig.read();
        modConfig.write(settings);

        foreach (KMSelectable button in planetButtons)
            button.OnInteract += delegate () { PressPlanet(button); return false; };
        starButton.OnInteract += delegate () { PressStar(); return false; };
        bomb.OnBombExploded += delegate { HandleDetonation(); };
    }

    private void Start()
    {
        StartCoroutine(DisableDummies());
        statusLight.SetActive(false);
        starCcw = rnd.Range(0, 2) != 0;
        planetSizes = Enumerable.Range(0, 3).ToList().Shuffle().ToArray();
        planetSpeeds = speeds.ToList().Shuffle().Take(3).ToArray();
        for (int i = 0; i < 3; i++)
        {
            planetSurfaces[i] = rnd.Range(0, 10);
            planetsCcw[i] = rnd.Range(0, 2) != 0;
            spinningCcw[i] = rnd.Range(0, 2) != 0;
            Debug.LogFormat("[Exoplanets #{0}] The {1} planet has an orbital period of {2}.", moduleId, positionNames[i], (int)planetSpeeds[i]);
            Debug.LogFormat("[Exoplanets #{0}] The {1} planet is {2}.", moduleId, positionNames[i], sizeNames[planetSizes[i]]);
        }
        for (int i = 0; i < 3; i++)
            orbits[i] = StartCoroutine(Orbit(pivots[i], i));
        foreach (Renderer planet in planets)
        {
            var ix = Array.IndexOf(planets, planet);
            planet.material.mainTexture = surfaces[planetSurfaces[ix]];
            var sizecord = sizes[planetSizes[ix]];
            planet.transform.localScale = new Vector3(sizecord, sizecord, sizecord);
            dummyPlanets[ix].transform.localScale = new Vector3(sizecord, sizecord, sizecord);
            planet.transform.localRotation = rnd.rotation;
            tilts[ix] = StartCoroutine(Spinning(planet, ix));
        }
        starSpinning = StartCoroutine(StarMovement());
        StartCoroutine(BackgroundMovement());
        GenerateSolution();
    }

    private void GenerateSolution()
    {
        if (!planetsCcw.Contains(true))
        {
            targetPlanet = 0;
            Debug.LogFormat("[Exoplanets #{0}] All planets are oribiting clockwise, so the initial target planet is the one closest to the star.", moduleId);
        }
        else if (!planetsCcw.Contains(false))
        {
            targetPlanet = 2;
            Debug.LogFormat("[Exoplanets #{0}] All planets are orbiting counterclockwise, so the initial target planet is the one farthest from the star.", moduleId);
        }
        else
        {
            if (planetsCcw.Count(x => x) == 2)
                targetPlanet = Array.IndexOf(planetsCcw, planetsCcw.First(x => !x));
            else
                targetPlanet = Array.IndexOf(planetsCcw, planetsCcw.First(x => x));
            Debug.LogFormat("[Exoplanets #{0}] The {1} planet is orbiting {2}, so it is the initial target planet.", moduleId, positionNames[targetPlanet], planetsCcw[targetPlanet] ? "counterclockwise" : "clockwise");
        }
        targetDigit = planetSurfaces[targetPlanet];
        startingTargetDigit = targetDigit;
        startingTargetPlanet = targetPlanet;
        Debug.LogFormat("[Exoplanets #{0}] The initial target digit is {1}.", moduleId, targetDigit);
        tablePosition = (tablePosition + bomb.GetBatteryCount() * (starCcw ? 7 : 1)) % 8;
        Debug.LogFormat("[Exoplanets #{0}] The star is rotating {1} and there are {2} batteries.", moduleId, !starCcw ? "clockwise" : "counterclockwise", bomb.GetBatteryCount());
        tableRing = targetPlanet;
        for (int i = 0; i < 3; i++)
            Modify(i);
        Debug.LogFormat("[Exoplanets #{0}] The final solution is to press the {1} planet on a {2}.", moduleId, positionNames[targetPlanet], targetDigit);
    }

    private void Modify(int j)
    {
        var offset = ((int)planetSpeeds[tableRing]);
        offset += planetSurfaces[tableRing];
        if (bomb.GetBatteryHolderCount() != 0)
            offset %= bomb.GetBatteryHolderCount();
        else
            offset %= 5;
        offset += bomb.GetPortCount();
        tablePosition = (tablePosition + offset * (planetsCcw[tableRing] ? 7 : 1)) % 8;
        var prevPlanet = targetPlanet;
        var prevDigit = targetDigit;
        Debug.LogFormat("[Exoplanets #{0}] Using rule {1}.", moduleId, table[tableRing][tablePosition]);
        switch (table[tableRing][tablePosition])
        {
            case "A":
                if (planetSizes[targetPlanet] == 2)
                    targetPlanet = Array.IndexOf(planetSizes, 0);
                else
                    targetPlanet = Array.IndexOf(planetSizes, planetSizes.Where(x => x > planetSizes[targetPlanet]).Min());
                break;
            case "B":
                targetDigit = (targetDigit + startingTargetDigit) % 10;
                break;
            case "C":
                if (planetSizes[targetPlanet] == 0)
                    targetPlanet = Array.IndexOf(planetSizes, 2);
                else
                    targetPlanet = Array.IndexOf(planetSizes, planetSizes.Where(x => x < planetSizes[targetPlanet]).Max());
                break;
            case "D":
                var differencesMax = new int[3];
                for (int i = 0; i < 3; i++)
                {
                    differencesMax[i] = planetSurfaces[targetPlanet] - planetSurfaces[i];
                    if (differencesMax[i] < 0)
                        differencesMax[i] *= -1;
                }
                targetPlanet = Array.IndexOf(differencesMax, differencesMax.Max());
                break;
            case "E":
                targetDigit = 9 - targetDigit;
                break;
            case "F":
                if (planetsCcw.Distinct().Count() == 1)
                    targetPlanet = 0;
                else
                    targetPlanet = Array.IndexOf(planetsCcw, planetsCcw.First(x => x != planetsCcw[targetPlanet]));
                break;
            case "G":
                targetPlanet = Array.IndexOf(planetsCcw, planetsCcw.First(x => x == planetsCcw[targetPlanet]));
                break;
            case "H":
                targetDigit = (targetDigit + bomb.GetSerialNumberNumbers().Sum()) % 10;
                break;
            case "I":
                if (planetSurfaces[targetPlanet] == planetSurfaces.Max())
                    targetPlanet = Array.IndexOf(planetSurfaces, planetSurfaces.Min());
                else
                    targetPlanet = Array.IndexOf(planetSurfaces, planetSurfaces.Where(x => x > planetSurfaces[targetPlanet]).Min());
                break;
            case "J":
                targetDigit = (targetDigit + bomb.GetModuleNames().Count()) % 10;
                break;
            case "K":
                targetPlanet--;
                if (targetPlanet == -1)
                    targetPlanet = 2;
                break;
            case "L":
                targetDigit = (targetDigit + bomb.GetSerialNumberNumbers().First()) % 10;
                break;
            case "M":
                var differencesMin = new int[3];
                for (int i = 0; i < 3; i++)
                {
                    differencesMin[i] = planetSurfaces[targetPlanet] - planetSurfaces[i];
                    if (differencesMin[i] < 0)
                        differencesMin[i] *= -1;
                }
                var differencesMinList = differencesMin.ToList();
                differencesMinList.Remove(differencesMinList[targetPlanet]);
                targetPlanet = Array.IndexOf(differencesMin, differencesMinList.Min());
                break;
            case "N":
                targetDigit = (targetDigit + bomb.GetSerialNumberNumbers().ToArray()[1]) % 10;
                break;
            case "O":
                if (targetPlanet == Array.IndexOf(planetSpeeds, planetSpeeds.Min()))
                    targetPlanet = Array.IndexOf(planetSpeeds, planetSpeeds.Max());
                else
                    targetPlanet = Array.IndexOf(planetSpeeds, planetSpeeds.Where(x => x < planetSpeeds[targetPlanet]).Max());
                break;
            case "P":
                targetPlanet = targetPlanet == 0 ? 2 : 0;
                break;
            case "Q":
                targetDigit = (targetDigit + bomb.GetPortCount()) % 10;
                break;
            case "R":
                targetDigit = (targetDigit + 5) % 10;
                break;
            case "S":
                if (targetPlanet == Array.IndexOf(planetSpeeds, planetSpeeds.Max()))
                    targetPlanet = Array.IndexOf(planetSpeeds, planetSpeeds.Min());
                else
                    targetPlanet = Array.IndexOf(planetSpeeds, planetSpeeds.Where(x => x > planetSpeeds[targetPlanet]).Min());
                break;
            case "T":
                targetDigit = (targetDigit + bomb.GetSerialNumberNumbers().Last()) % 10;
                break;
            case "U":
                targetPlanet = (targetPlanet + 1) % 3;
                break;
            case "V":
                targetDigit = (targetDigit + bomb.GetBatteryCount()) % 10;
                break;
            case "W":
                var indicators = bomb.GetIndicators().SelectMany(x => x.ToUpperInvariant().ToCharArray());
                var count = 0;
                foreach (Char c in indicators)
                    if (!"AEIOU".Contains(c))
                        count++;
                targetDigit = (targetDigit + count) % 10;
                break;
            default:
                if (planetSurfaces[targetPlanet] == planetSurfaces.Min())
                    targetPlanet = Array.IndexOf(planetSurfaces, planetSurfaces.Max());
                else
                    targetPlanet = Array.IndexOf(planetSurfaces, planetSurfaces.Where(x => x < planetSurfaces[targetPlanet]).Max());
                break;
        }
        if (targetPlanet != prevPlanet)
            Debug.LogFormat("[Exoplanets #{0}] The target planet is now the {1} planet.", moduleId, positionNames[targetPlanet]);
        if (targetDigit != prevDigit)
            Debug.LogFormat("[Exoplanets #{0}] The target digit is now {1}.", moduleId, targetDigit);
        if (j != 2)
        {
            tableRing--;
            if (tableRing == -1)
                tableRing = 2;
            Debug.LogFormat("[Exoplanets #{0}] Moving inward...", moduleId);
        }
    }

    void PressPlanet(KMSelectable button)
    {
        if (moduleSolved)
            return;
        var ix = Array.IndexOf(planetButtons, button);
        var submittedTime = ((int)bomb.GetTime()) % 10;
        var oridinals = new string[] { "1st", "2nd", "3rd" };
        Debug.LogFormat("[Exoplanets #{0}] You pressed the {1} planet.", moduleId, oridinals[ix]);
        var planetCorrect = targetPlanet == ix;
        var timeCorrect = targetDigit == submittedTime;
        if (planetCorrect && timeCorrect)
        {
            module.HandlePass();
            moduleSolved = true;
            if (ambianceRef != null && settings.playAmbiance)
            {
                ambianceRef.StopSound();
                ambianceRef = null;
            }
            Debug.LogFormat("[Exoplanets #{0}] That was correct. Module solved!", moduleId);
            audio.PlaySoundAtTransform("explosion", transform);
            StartCoroutine(SolveAnimation());
        }
        else
        {
            if (!planetCorrect && !timeCorrect)
                Debug.LogFormat("[Exoplanets #{0}] Neither the planet nor the time were correct.", moduleId);
            else if (!planetCorrect && timeCorrect)
                Debug.LogFormat("[Exoplanets #{0}] The planet wasn’t correct, but the time was.", moduleId);
            else
                Debug.LogFormat("[Exoplanets #{0}] The planet was correct, but the time wasn’t.", moduleId);
            module.HandleStrike();
            Debug.LogFormat("[Exoplanets #{0}] Strike!", moduleId);
        }
    }

    void PressStar()
    {
        if (isMoving && !moduleSolved)
            return;
        if (moduleSolved)
            return;
        else
        {
            foreach (Renderer planet in planets)
            {
                var ix = Array.IndexOf(planets, planet);
                if (!planetsHidden)
                    savedPositions[ix] = planet.transform.localPosition;
                StartCoroutine(MovePlanets(planet, ix));
            }
        }
    }

    IEnumerator MovePlanets(Renderer planet, int ix)
    {
        isMoving = true;
        var elapsed = 0f;
        var duration = 1f;
        while (elapsed < duration)
        {
            if (!planetsHidden)
                planet.transform.localPosition = new Vector3(Easing.InOutQuad(elapsed, savedPositions[ix].x, 0, duration), Easing.InOutQuad(elapsed, savedPositions[ix].y, .0314f, duration), Easing.InOutQuad(elapsed, savedPositions[ix].z, 0, duration));
            else
                planet.transform.localPosition = new Vector3(Easing.InOutQuad(elapsed, 0, savedPositions[ix].x, duration), Easing.InOutQuad(elapsed, .0314f, savedPositions[ix].y, duration), Easing.InOutQuad(elapsed, 0, savedPositions[ix].z, duration));
            yield return null;
            elapsed += Time.deltaTime;
        }
        isMoving = false;
        planetsHidden = !planetsHidden;
    }

    IEnumerator Orbit(Transform pivot, int ix)
    {
        var startAngle = rnd.Range(0, 360);
        var elapsed = 0f;
        var speed = planetSpeeds[ix];
        if (planetsCcw[ix])
            speed = -speed;
        while (true)
        {
            pivot.localEulerAngles = new Vector3(0, elapsed / speed * 360 + startAngle, 0);
            yield return null;
            elapsed += Time.deltaTime;
        }
    }

    IEnumerator Spinning(Renderer planet, int ix)
    {
        var startAngle = rnd.Range(0, 360);
        var pX = planet.transform.localEulerAngles.x;
        var pZ = planet.transform.localEulerAngles.z;
        var elapsed = 0f;
        var speed = new float[] { 4f, 6f, 7f, 8f }.PickRandom();
        if (!planetsCcw[ix])
            speed = -speed;
        while (true)
        {
            planet.transform.localEulerAngles = new Vector3(pX, elapsed / speed * 360 + startAngle, pZ);
            yield return null;
            elapsed += Time.deltaTime;
        }
    }

    IEnumerator StarMovement()
    {
        var starScrollSpeed = .02f;
        if (starCcw)
            starScrollSpeed = -starScrollSpeed;
        while (true)
        {
            var offsetStar = Time.time * starScrollSpeed;
            star.material.mainTextureOffset = new Vector2(offsetStar, 0f);
            yield return null;
        }
    }

    IEnumerator BackgroundMovement()
    {
        var horizontalScrollSpeed = .001f;
        var verticalScrollSpeed = .001f;
        while (true)
        {
            var offsetY = Time.time * horizontalScrollSpeed;
            var offsetZ = Time.time * verticalScrollSpeed;
            background.material.mainTextureOffset = new Vector2(offsetY, offsetZ);
            yield return null;
        }
    }

    private IEnumerator SolveAnimation()
    {
        var elapsed = 0f;
        var duration = 4f;
        for (int i = 0; i < 3; i++)
        {
            savedPositions[i] = planets[i].transform.localPosition;
            StopCoroutine(orbits[i]);
            StopCoroutine(tilts[i]);
        }
        StopCoroutine(starSpinning);
        while (elapsed < duration)
        {
            var fade = Mathf.Lerp(1f, 0f, elapsed / duration);
            foreach (Renderer planet in planets)
                planet.material.color = new Color(fade, fade, fade);
            star.material.color = new Color(fade, fade, fade);
            yield return null;
            elapsed += Time.deltaTime;
        }
        for (int i = 0; i < 3; i++)
        {
            planets[i].gameObject.SetActive(false);
            dummyPlanets[i].transform.SetParent(pivots[i], false);
            dummyPlanets[i].gameObject.SetActive(true);
            dummyPlanets[i].transform.localPosition = savedPositions[i];
        }
        star.gameObject.SetActive(false);
        dummyStar.gameObject.SetActive(true);
        elapsed = 0f;
        duration = 3f;
        while (elapsed < duration)
        {
            var fade = Mathf.Lerp(1f, 0f, elapsed / duration);
            dummyStar.material.color = new Color(0f, 0f, 0f, fade);
            foreach (Renderer planet in dummyPlanets)
                planet.material.color = new Color(0f, 0f, 0f, fade);
            yield return null;
            elapsed += Time.deltaTime;
        }
        dummyStar.gameObject.SetActive(false);
        foreach (Renderer planet in dummyPlanets)
            planet.gameObject.SetActive(false);
        yield return new WaitForSeconds(.75f);
        background.material.color = solveColor;
        audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.CorrectChime, transform);
    }

    private IEnumerator DisableDummies()
    {
        yield return null;
        dummyStar.gameObject.SetActive(false);
        foreach (Renderer planet in dummyPlanets)
            planet.gameObject.SetActive(false);
        if (settings.playAmbiance)
            ambianceRef = audio.PlaySoundAtTransformWithRef("ambiance", star.transform);
        yield return new WaitForSeconds(rnd.Range(.5f, 1.5f));
        dummyStar.gameObject.SetActive(false);
        foreach (Renderer planet in dummyPlanets)
            planet.gameObject.SetActive(false);
    }

    private void HandleDetonation()
    {
        StopAllCoroutines();
        if (ambianceRef != null && settings.playAmbiance)
        {
            ambianceRef.StopSound();
            ambianceRef = null;
        }
    }

    // Twitch Plays
#pragma warning disable 414
    private readonly string TwitchHelpMessage = @"!{0} <inner/middle/outer> <0-9> [Presses the planet in that position on that digit] | !{0} star [Presses the star]";
#pragma warning restore 414

    private IEnumerator ProcessTwitchCommand(string input)
    {
        var cmd = input.ToLowerInvariant();
        if (cmd == "star")
        {
            yield return null;
            starButton.OnInteract();
            yield break;
        }
        else if (positionNames.Any(x => cmd.StartsWith(x + " ")))
        {
            var processedCmd = cmd.Split(' ').ToArray();
            var digits = new string[] { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" };
            if (processedCmd.Length != 2 || !digits.Any(d => processedCmd[1] == d))
                yield break;
            else
            {
                yield return null;
                while (((int)bomb.GetTime()) % 10 != Array.IndexOf(digits, processedCmd[1]))
                    yield return null;
                planetButtons[Array.IndexOf(positionNames, processedCmd[0])].OnInteract();
            }
        }
        else
            yield break;
    }

    private IEnumerator TwitchHandleForcedSolve()
    {
        if (planetsHidden)
            starButton.OnInteract();
        yield return new WaitForSeconds(1.75f);
        while (((int)bomb.GetTime()) % 10 != targetDigit)
            yield return null;
        planetButtons[targetPlanet].OnInteract();
    }
}
