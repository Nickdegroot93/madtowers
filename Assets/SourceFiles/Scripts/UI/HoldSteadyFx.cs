using TMPro;
using UnityEngine;

/// <summary>Reads the verification clock; never advances it or banks a rung. All visual
/// motion is unscaled, including when a pause sheet covers the live verification.</summary>
public sealed class HoldSteadyFx : MonoBehaviour
{
    private RectTransform _cube, _digitRoot, _left, _right;
    private TextMeshProUGUI _digit, _shadow;
    private float _remaining, _duration, _width, _beatAge, _age;
    private int _second = -1;

    public static HoldSteadyFx Attach(GameObject root, RectTransform cube, RectTransform digitRoot,
        TextMeshProUGUI digit, TextMeshProUGUI shadow, RectTransform left, RectTransform right,
        float duration, float width)
    {
        var fx = root.AddComponent<HoldSteadyFx>();
        fx._cube = cube; fx._digitRoot = digitRoot; fx._digit = digit; fx._shadow = shadow;
        fx._left = left; fx._right = right; fx._duration = duration; fx._width = width;
        return fx;
    }

    public void SetRemaining(float seconds)
    {
        _remaining = Mathf.Max(0f, seconds);
        int second = Mathf.CeilToInt(_remaining);
        if (second == _second) return;
        _second = second;
        _digit.text = second.ToString();
        if (_shadow != null) _shadow.text = _digit.text;
        _beatAge = 0f;
    }

    private void Update()
    {
        if (_cube != null) HudVisualStyle.PlaceHold(_cube.parent as RectTransform);
        _age += Time.unscaledDeltaTime;
        _beatAge += Time.unscaledDeltaTime;
        float tension = 1f - Mathf.Clamp01(_remaining / _duration);
        float last = 1f - Mathf.Clamp01(_remaining);
        float beat = Mathf.Sin(Mathf.Min(_beatAge / .44f, 1f) * Mathf.PI) * Mathf.Exp(-_beatAge * 5f);
        float inhale = Mathf.Sin(_age * 3.2f) * .012f * (1f - last);
        if (_cube != null)
        {
            float scale = 1f + inhale + beat * (.045f + tension * .065f) + last * .075f;
            _cube.localScale = new Vector3(scale + beat * .035f, scale - beat * .035f, 1f);
            _cube.anchoredPosition = new Vector2(0f, Mathf.Sin(_age * 2f) * 3f * (1f - tension));
        }
        float digitScale = 1f + beat * .06f + last * .035f;
        _digitRoot.localScale = new Vector3(digitScale, digitScale, 1f);
        _digit.color = HudVisualStyle.Current.Ink;
        if (_shadow != null) _shadow.color = Color.clear;
        // The fill remains an exact linear picture of the rules clock. Tension is carried
        // by thickness and the cube, never by pretending there is less time left.
        float width = _width * Mathf.Clamp01(_remaining / _duration);
        float height = 3f + tension + beat;
        _left.sizeDelta = _right.sizeDelta = new Vector2(width, height);
    }
}
