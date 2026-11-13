using UnityEngine;

namespace NargTrophy.Banners;

/// <summary>
/// Planted war standard only (docs/05): how it ends. No light: a glow was tried and dropped (2026-11-13).
///
/// Photosensitivity rule (agreed 2026-11-13, after an in-game test): nothing here may flash or blink. No
/// renderer is ever switched on and off; everything moves smoothly.
/// - Last <see cref="SinkSeconds"/> (still active): it sinks smoothly to <see cref="PoofDepth"/> of its height,
///   about three-quarters in (agreed in game, 2026-11-13).
/// - At expiry: a gold "poof" at the part still showing, the creature's own corpse-disappearing effect tinted
///   (BannerVisuals.MakePoof). One burst, not a flash. Only played if this client saw the standard alive, so
///   loading into an area with an already-expired standard doesn't set it off.
/// - Then the rest sinks away under the smoke over <see cref="FinishSeconds"/>. The owner removes it
///   <see cref="TrophyBanner.RemoveDelay"/> seconds after expiry.
/// Visual only. Time left comes from the ZDO (server time), so every client shows the same thing.
/// </summary>
public class StandardExpiry : MonoBehaviour
{
    private const float SinkSeconds = 4f;     // before expiry
    private const float PoofDepth = 0.75f;    // share of the height sunk when it poofs
    private const float FinishSeconds = 0.6f; // after expiry; must finish before TrophyBanner.RemoveDelay
    private const float CheckSeconds = 0.25f;

    /// <summary>Tinted corpse-removal effect, played once at expiry. A prefab, not a child.</summary>
    public GameObject? m_poof;

    private TrophyBanner? _banner;
    private float _secondsLeft = float.MaxValue;
    private float _nextCheck;
    private Vector3 _restPosition;
    private float _sinkDepth = -1f;
    private bool _seenAlive;
    private bool _poofed;

    private void Awake() => _banner = GetComponent<TrophyBanner>();

    private void Start() => _restPosition = transform.position;

    /// <summary>
    /// Safety net: if the standard is removed after expiry before Update played the poof (a hitch, or a
    /// removal by other means), poof here. Not for a zone unload or logout (not expired then).
    /// </summary>
    private void OnDestroy()
    {
        if (_banner == null || !_banner.IsExpired() || ZNetScene.instance == null) return;
        Poof();
    }

    private void Update()
    {
        if (Time.time >= _nextCheck)
        {
            _nextCheck = Time.time + CheckSeconds;
            _secondsLeft = _banner != null && _banner.SecondsLeft() is float left ? left : float.MaxValue;
        }
        else
        {
            _secondsLeft -= Time.deltaTime;
        }

        if (_secondsLeft > 0f && _secondsLeft < float.MaxValue) _seenAlive = true;

        if (_secondsLeft > SinkSeconds)
        {
            Sink(0f);
            return;
        }

        if (_secondsLeft > 0f)
        {
            // Still active: eases down to PoofDepth (slow start, so it's a settle, not a jump).
            float t = Mathf.Clamp01(1f - _secondsLeft / SinkSeconds);
            Sink(PoofDepth * t * t);
            return;
        }

        // Expired: poof where it stands now, then the rest sinks away under the smoke.
        Sink(PoofDepth);
        Poof();
        float finish = Mathf.Clamp01(-_secondsLeft / FinishSeconds);
        Sink(Mathf.Lerp(PoofDepth, 1f, finish));
    }

    /// <summary>Once, at the middle of the part still above ground, only if this client saw it alive.</summary>
    private void Poof()
    {
        if (_poofed || !_seenAlive || m_poof == null) return;
        _poofed = true;
        float showing = _sinkDepth > 0f ? _sinkDepth * (1f - PoofDepth) : 0.6f;
        Instantiate(m_poof, _restPosition + Vector3.up * (showing * 0.5f), Quaternion.identity);
    }

    /// <summary>0 = standing, 1 = fully underground. Moves this client's copy only; the piece isn't synced by position.</summary>
    private void Sink(float amount)
    {
        if (amount <= 0f)
        {
            if (_sinkDepth > 0f) transform.position = _restPosition;
            return;
        }
        if (_sinkDepth < 0f)
        {
            _restPosition = transform.position;
            _sinkDepth = BannerVisuals.TryGetMeshBounds(transform, transform, out Bounds bounds, out _)
                ? bounds.max.y * transform.lossyScale.y + 0.1f
                : 2.5f;
        }
        transform.position = _restPosition + Vector3.down * (_sinkDepth * amount);
    }
}
