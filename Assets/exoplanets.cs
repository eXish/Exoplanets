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

    private static readonly float[] sizes = new float[3] { .015f, .025f, .035f };
    private static readonly float[] speeds = new float[] { 5f, 10f, 20f, 40f };
    private int[] planetSizes = new int[3];
    private float[] planetSpeeds = new float[3];
    private int[] planetSurfaces = new int[3];
    private bool[] planetsCcw = new bool[3];

    private bool starCcw;
    private int batteryOffset;
    private int targetPlanet;
    private int targetDigit;
    private int startingTargetPlanet;
    private int startingTargetDigit;
    private int tablePosition;
    private int tableRing;
    private static readonly string[][] table = new string[][] {
        new string[] { "A", "B", "C", "D", "E", "F", "G", "H" },
        new string[] { "I", "J", "K", "L", "M", "N", "O", "P" },
        new string[] { "Q", "R", "S", "T", "U", "V", "W", "X" }
    };

    private static readonly string[] positionNames = new string[] { "inner", "middle", "outer" };
    private Vector3[] savedPositions = new Vector3[3];
    private KMAudio.KMAudioRef ambianceRef;
    private Coroutine starSpinning;
    private Coroutine[] orbits = new Coroutine[3];
    private Coroutine[] tilts = new Coroutine[3];
    private bool planetsHidden;
    private bool isMoving;

    static int moduleIdCounter = 1;
    int moduleId;
    private bool moduleSolved;

    void Awake()
    {
        moduleId = moduleIdCounter++;
        foreach (KMSelectable button in planetButtons)
            button.OnInteract += delegate () { PressPlanet(button); return false; };
        starButton.OnInteract += delegate () { PressStar(); return false; };
    }

    void Start()
    {
        StartCoroutine(DisableDummies());
        statusLight.SetActive(false);
        batteryOffset = !starCcw ? 1 : -1;
        planetSizes = Enumerable.Range(0, 3).ToList().Shuffle().ToArray();
        planetSpeeds = speeds.ToList().Shuffle().Take(3).ToArray();
        for (int i = 0; i < 3; i++)
        {
            planetSurfaces[i] = rnd.Range(0, 10);
            planetsCcw[i] = rnd.Range(0, 2) != 0;
            Debug.LogFormat("[Exoplanets #{0}] The {1} planet has an angular velocity of {2}.", moduleId, positionNames[i], (int)planetSpeeds[i]);
        }
        starCcw = rnd.Range(0, 2) != 0;
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
        GenerateSolution();
    }

    void GenerateSolution()
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
        Debug.LogFormat("[Exoplanets #{0}] The initial target digit is {1}.", moduleId, targetDigit);
        for (int i = 0; i < bomb.GetBatteryCount(); i++)
        {
            tablePosition += batteryOffset;
            if (tablePosition == -1)
                tablePosition = 7;
            tablePosition %= 8;
        }
        Debug.LogFormat("[Exoplanets #{0}] The star is rotating {1} and there are {2} batteries.", moduleId, !starCcw ? "clockwise" : "counterclockwise", bomb.GetBatteryCount());
        tableRing = targetPlanet;
        for (int i = 0; i < 3; i++)
            Modify(i);
        Debug.LogFormat("[Exoplanets #{0}] The final solution is to press the {1} planet on a {2}.", moduleId, positionNames[targetPlanet], targetDigit);
    }

    void Modify(int j)
    {
        var offset = ((int)planetSpeeds[tableRing]);
        if (bomb.GetBatteryHolderCount() != 0)
             offset %= bomb.GetBatteryHolderCount();
        else
            offset %= 5;
        for (int i = 0; i < offset; i++)
        {
            if (!planetsCcw[tableRing])
                tablePosition++;
            else
                tablePosition--;
            if (tablePosition == -1)
                tablePosition = 7;
            tablePosition %= 8;
        }
        var prevPlanet = targetPlanet;
        var prevDigit = targetDigit;
        Debug.LogFormat("[Exoplanets #{0}] Using rule {1}.", moduleId, table[tableRing][tablePosition]);
        switch(table[tableRing][tablePosition])
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
                if (planetsCcw.Distinct().Count() == 0)
                    targetPlanet = 1;
                else
                    targetPlanet = Array.IndexOf(planetsCcw, planetsCcw.First(x => x == planetsCcw[targetPlanet]));
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
                if (targetPlanet == Array.IndexOf(planetSpeeds, planetSpeeds.Max()))
                    targetPlanet = Array.IndexOf(planetSpeeds, planetSpeeds.Min());
                else
                    targetPlanet = Array.IndexOf(planetSpeeds, planetSpeeds.Where(x => x > planetSpeeds[targetPlanet]).Min());
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
                if (targetPlanet == Array.IndexOf(planetSpeeds, planetSpeeds.Min()))
                    targetPlanet = Array.IndexOf(planetSpeeds, planetSpeeds.Max());
                else
                    targetPlanet = Array.IndexOf(planetSpeeds, planetSpeeds.Where(x => x < planetSpeeds[targetPlanet]).Max());
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
                    targetPlanet = Array.IndexOf(planetSurfaces, planetSurfaces.Where(x => x < planetSurfaces[targetPlanet]).Min());
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
            if (ambianceRef != null)
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
        var speed = planetSpeeds[ix];
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

    IEnumerator BackgroundMoveMent()
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

    IEnumerator SolveAnimation()
    {
        var elapsed = 0f;
        var duration = 6f;
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
        elapsed = 0f;
        duration = 3f;
        while (elapsed < duration)
        {
            var fade = Mathf.Lerp(1f, 0f, elapsed / duration);
            star.material.color = new Color(fade, fade, fade);
            yield return null;
            elapsed += Time.deltaTime;
        }
        star.gameObject.SetActive(false);
        dummyStar.gameObject.SetActive(true);
        elapsed = 0f;
        duration = 3.5f;
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
        yield return new WaitForSeconds(2f);
        background.material.color = solveColor;
        audio.PlayGameSoundAtTransform(KMSoundOverride.SoundEffect.CorrectChime, transform);
    }

    IEnumerator DisableDummies()
    {
        yield return null;
        ambianceRef = audio.PlaySoundAtTransformWithRef("ambiance", star.transform);
        dummyStar.gameObject.SetActive(false);
        foreach (Renderer planet in dummyPlanets)
            planet.gameObject.SetActive(false);
    }
}
