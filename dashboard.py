"""
Soccer Simulation — Agent Emotional Analysis Dashboard
=======================================================
Requirements:
    pip install streamlit pandas plotly

Run:
    streamlit run dashboard.py
"""

import io
import pandas as pd
import plotly.graph_objects as go
import streamlit as st

# ── Page config ───────────────────────────────────────────────────────────────
st.set_page_config(
    page_title="Simulation Analysis",
    page_icon="⚽",
    layout="wide",
    initial_sidebar_state="collapsed",
)

# ── Colour maps ───────────────────────────────────────────────────────────────
EMOTION_COLOURS = {
    "Joy":             "#F9CA24",
    "Gratification":   "#6AB04C",
    "Satisfaction":    "#22A6B3",
    "Distress":        "#E55039",
    "Disappointment":  "#EB4D4B",
    "FearsConfirmed":  "#8E44AD",
}
ACTION_COLOURS = {
    "Celebrate":     "#F9CA24",
    "Chant":         "#6AB04C",
    "FormGroup":     "#22A6B3",
    "ComfortAlly":   "#48DBFB",
    "Boo":           "#E55039",
    "CalmSituation": "#9B59B6",
    "WatchCalmly":   "#95A5A6",
}

OCEAN_TRAITS = ["Openness", "Conscientiousness", "Extraversion", "Agreeableness", "Neuroticism"]

# ── CSS ───────────────────────────────────────────────────────────────────────
st.markdown("""
<style>
    h1 { color: #1F3864; }
    .sec {
        font-size: 12px; font-weight: 700; letter-spacing: .1em;
        text-transform: uppercase; color: #2E75B6;
        border-bottom: 2px solid #2E75B6;
        padding-bottom: 4px; margin: 0 0 14px 0;
    }
    .filter-note {
        font-size: 12px; color: #888; font-style: italic; margin-bottom: 10px;
    }
    div[data-testid="metric-container"] {
        background: #f4f8fd;
        border: 1px solid #d0e4f7;
        border-radius: 8px;
        padding: 10px 14px;
    }
</style>
""", unsafe_allow_html=True)


# ── Helpers ───────────────────────────────────────────────────────────────────
def get_octant(p, a, d):
    ps  = "+P" if p >= 0 else "-P"
    as_ = "+A" if a >= 0 else "-A"
    ds  = "+D" if d >= 0 else "-D"
    name = {
        "+P+A+D": "Exuberant",  "+P+A-D": "Dependent",
        "+P-A+D": "Relaxed",    "+P-A-D": "Docile",
        "-P+A+D": "Hostile",    "-P+A-D": "Anxious",
        "-P-A+D": "Disdainful", "-P-A-D": "Bored",
    }
    key = ps + as_ + ds
    return f"{key} {name.get(key, '')}"


@st.cache_data
def load_df(content: bytes) -> pd.DataFrame:
    df = pd.read_csv(io.BytesIO(content), sep=";", decimal=",", encoding="utf-8-sig")
    num_cols = [
        "Openness", "Conscientiousness", "Extraversion",
        "Agreeableness", "Neuroticism", "Stability", "EmotionIntensity",
        "PrevMood_P", "PrevMood_A", "PrevMood_D",
        "NewMood_P",  "NewMood_A",  "NewMood_D",
    ]
    for c in num_cols:
        if c in df.columns:
            df[c] = pd.to_numeric(df[c], errors="coerce")

    df["DeltaP"] = df["NewMood_P"] - df["PrevMood_P"]
    df["DeltaA"] = df["NewMood_A"] - df["PrevMood_A"]
    df["DeltaD"] = df["NewMood_D"] - df["PrevMood_D"]
    df["PrevOctant"] = df.apply(
        lambda r: get_octant(r["PrevMood_P"], r["PrevMood_A"], r["PrevMood_D"]), axis=1)
    df["NewOctant"] = df.apply(
        lambda r: get_octant(r["NewMood_P"], r["NewMood_A"], r["NewMood_D"]), axis=1)
    df["OctantChanged"] = df["PrevOctant"] != df["NewOctant"]
    return df


def prob_bar(series: pd.Series, colour_map: dict, height=260) -> go.Figure:
    """Plain horizontal probability bar chart (used for emotions)."""
    counts = series.value_counts()
    if counts.empty:
        return go.Figure()
    probs   = counts / counts.sum()
    labels  = probs.index.tolist()
    values  = probs.values.tolist()
    colours = [colour_map.get(l, "#95A5A6") for l in labels]
    fig = go.Figure(go.Bar(
        x=values, y=labels,
        orientation="h",
        marker_color=colours,
        text=[f"{v:.1%}" for v in values],
        textposition="outside",
        cliponaxis=False,
    ))
    fig.update_layout(
        height=height,
        margin=dict(l=0, r=60, t=10, b=10),
        xaxis=dict(
            tickformat=".0%",
            range=[0, min(1.15, max(values) * 1.35)],
            showgrid=True, gridcolor="#eee",
        ),
        yaxis=dict(autorange="reversed"),
        plot_bgcolor="white",
        paper_bgcolor="white",
        showlegend=False,
        font=dict(size=12),
    )
    return fig


def action_prob_bar(sub: pd.DataFrame, colour_map: dict, height=260) -> go.Figure:
    """
    Horizontal probability bar chart for actions.

    Hovering over a bar shows the mean OCEAN trait values of the agents
    who performed that action under the current filters.

    Each bar's tooltip displays:
      • Action name + probability + agent count
      • Mean O / C / E / A / N for that action group
    """
    if sub.empty:
        return go.Figure()

    counts = sub["Action"].value_counts()
    probs  = counts / counts.sum()
    total  = counts.sum()

    # Mean OCEAN per action group
    ocean_means = (
        sub.groupby("Action")[OCEAN_TRAITS]
           .mean()
           .reindex(counts.index)   # keep same order as counts
    )

    labels  = counts.index.tolist()
    values  = probs.values.tolist()
    colours = [colour_map.get(l, "#95A5A6") for l in labels]

    # Build one hover string per action
    hover_texts = []
    for action in labels:
        n   = counts[action]
        p   = probs[action]
        row = ocean_means.loc[action]
        tip = (
            f"<b>{action}</b><br>"
            f"P(action) = {p:.1%}  ({n} agents)<br>"
            f"<br>"
            f"<b>Mean OCEAN traits</b><br>"
            f"  Openness          {row['Openness']:.3f}<br>"
            f"  Conscientiousness {row['Conscientiousness']:.3f}<br>"
            f"  Extraversion      {row['Extraversion']:.3f}<br>"
            f"  Agreeableness     {row['Agreeableness']:.3f}<br>"
            f"  Neuroticism       {row['Neuroticism']:.3f}"
        )
        hover_texts.append(tip)

    fig = go.Figure(go.Bar(
        x=values,
        y=labels,
        orientation="h",
        marker_color=colours,
        text=[f"{v:.1%}" for v in values],
        textposition="outside",
        cliponaxis=False,
        # Replace default tooltip entirely with our custom one
        hovertemplate="%{customdata}<extra></extra>",
        customdata=hover_texts,
    ))
    fig.update_layout(
        height=height,
        margin=dict(l=0, r=60, t=10, b=10),
        xaxis=dict(
            tickformat=".0%",
            range=[0, min(1.15, max(values) * 1.35)],
            showgrid=True, gridcolor="#eee",
        ),
        yaxis=dict(autorange="reversed"),
        plot_bgcolor="white",
        paper_bgcolor="white",
        showlegend=False,
        font=dict(size=12),
        hoverlabel=dict(
            bgcolor="white",
            bordercolor="#d0e4f7",
            font_size=13,
            font_family="monospace",   # monospace keeps the trait columns aligned
        ),
    )
    return fig


def multiselect_all(label, options, key):
    """Multiselect preceded by an All toggle."""
    use_all = st.checkbox(f"All {label}", value=True, key=f"all_{key}")
    if use_all:
        return list(options)
    chosen = st.multiselect(label, options, default=list(options), key=key)
    return chosen if chosen else list(options)


def delta_card(col, label, value):
    """Coloured delta metric card."""
    arrow  = "▲" if value > 0 else ("▼" if value < 0 else "—")
    colour = "#27AE60" if value > 0 else ("#E74C3C" if value < 0 else "#888")
    col.markdown(
        f"<div style='text-align:center;padding:10px;background:#f4f8fd;"
        f"border:1px solid #d0e4f7;border-radius:8px'>"
        f"<div style='font-size:11px;color:#555;font-weight:600'>{label}</div>"
        f"<div style='font-size:22px;font-weight:700;color:{colour}'>"
        f"{arrow} {abs(value):.4f}</div></div>",
        unsafe_allow_html=True,
    )


# ═════════════════════════════════════════════════════════════════════════════
# Upload
# ═════════════════════════════════════════════════════════════════════════════
st.title("⚽  Soccer Simulation — Emotional Agent Analysis")

uploaded = st.file_uploader("Upload simulation CSV", type="csv")
if not uploaded:
    st.info("Upload a CSV file to begin.")
    st.stop()

df = load_df(uploaded.read())
st.success(
    f"Loaded **{len(df):,} records** · "
    f"{df['AgentID'].nunique()} agents · "
    f"{df['Personality'].nunique()} personality types · "
    f"{df['EventID'].nunique()} event types"
)
st.markdown("---")

ALL_PERS  = sorted(df["Personality"].unique())
ALL_EVTS  = sorted(df["EventID"].unique())
ALL_EMOS  = sorted(df["Emotion"].unique())
ALL_ACTS  = sorted(df["Action"].unique())
ALL_OCT   = sorted(df["NewOctant"].unique())

# ═════════════════════════════════════════════════════════════════════════════
# Data explorer — independent filtered table
# ═════════════════════════════════════════════════════════════════════════════
st.markdown(
    '<div class="sec">Data explorer — independent filtered record table</div>',
    unsafe_allow_html=True,
)
st.markdown(
    '<div class="filter-note">'
    'Filters: Personality · Event · Action · Emotion · Mood Octant. '
    'Independent from all sections above.'
    '</div>',
    unsafe_allow_html=True,
)

dt1, dt2 = st.columns(2)
dt3, dt4, dt5 = st.columns(3)

with dt1: sel_t_pers = multiselect_all("Personalities", ALL_PERS, "dt_pers")
with dt2: sel_t_evts = multiselect_all("Events",        ALL_EVTS, "dt_evt")
with dt3: sel_t_acts = multiselect_all("Actions",       ALL_ACTS, "dt_act")
with dt4: sel_t_emos = multiselect_all("Emotions",      ALL_EMOS, "dt_emo")
with dt5: sel_t_oct  = multiselect_all("Mood Octants",  ALL_OCT,  "dt_oct")

tbl = df[
    df["Personality"].isin(sel_t_pers) &
    df["EventID"].isin(sel_t_evts) &
    df["Action"].isin(sel_t_acts) &
    df["Emotion"].isin(sel_t_emos) &
    df["NewOctant"].isin(sel_t_oct)
].reset_index(drop=True)

DISPLAY_COLS = [
    "AgentID", "AgentName", "Team", "Personality",
    "Openness", "Conscientiousness", "Extraversion", "Agreeableness",
    "Neuroticism", "Stability",
    "EventID", "Emotion", "EmotionIntensity",
    "PrevMood_P", "PrevMood_A", "PrevMood_D", "PrevOctant",
    "NewMood_P",  "NewMood_A",  "NewMood_D",  "NewOctant",
    "DeltaP", "DeltaA", "DeltaD", "OctantChanged",
    "Action",
    "Time"
]
tbl_view = tbl[[c for c in DISPLAY_COLS if c in tbl.columns]]

st.markdown(f"**{len(tbl_view):,} records** match the current filters.")

st.dataframe(
    tbl_view,
    use_container_width=True,
    hide_index=True,
    height=420,
    column_config={
        "EmotionIntensity": st.column_config.ProgressColumn(
            "Intensity", min_value=0, max_value=1, format="%.3f"),
        "OctantChanged": st.column_config.CheckboxColumn("Octant shifted?"),
        "DeltaP":  st.column_config.NumberColumn("ΔP",  format="%.4f"),
        "DeltaA":  st.column_config.NumberColumn("ΔA",  format="%.4f"),
        "DeltaD":  st.column_config.NumberColumn("ΔD",  format="%.4f"),
        "PrevMood_P": st.column_config.NumberColumn("Prev P", format="%.3f"),
        "PrevMood_A": st.column_config.NumberColumn("Prev A", format="%.3f"),
        "PrevMood_D": st.column_config.NumberColumn("Prev D", format="%.3f"),
        "NewMood_P":  st.column_config.NumberColumn("New P",  format="%.3f"),
        "NewMood_A":  st.column_config.NumberColumn("New A",  format="%.3f"),
        "NewMood_D":  st.column_config.NumberColumn("New D",  format="%.3f"),
        "Openness":          st.column_config.NumberColumn(format="%.3f"),
        "Conscientiousness": st.column_config.NumberColumn(format="%.3f"),
        "Extraversion":      st.column_config.NumberColumn(format="%.3f"),
        "Agreeableness":     st.column_config.NumberColumn(format="%.3f"),
        "Neuroticism":       st.column_config.NumberColumn(format="%.3f"),
        "Stability":         st.column_config.NumberColumn(format="%.3f"),
    },
)

# ═════════════════════════════════════════════════════════════════════════════
# General metrics
# ═════════════════════════════════════════════════════════════════════════════
st.markdown(
    '<div class="sec">General metrics — simulation-wide frequencies</div>',
    unsafe_allow_html=True,
)
st.markdown(
    '<div class="filter-note">No filters — reflects all records in the uploaded file.</div>',
    unsafe_allow_html=True,
)

g1, g2, g3 = st.columns(3)

with g1:
    st.markdown("**Most common new mood octants**")
    o = df["NewOctant"].value_counts().reset_index()
    o.columns = ["Octant", "Count"]
    o["Share"] = (o["Count"] / len(df)).map(lambda x: f"{x:.1%}")
    st.dataframe(o, use_container_width=True, hide_index=True, height=300)

with g2:
    st.markdown("**Most common emotions**")
    e = df["Emotion"].value_counts().reset_index()
    e.columns = ["Emotion", "Count"]
    e["Share"] = (e["Count"] / len(df)).map(lambda x: f"{x:.1%}")
    st.dataframe(e, use_container_width=True, hide_index=True, height=300)

with g3:
    st.markdown("**Most common actions**")
    a = df["Action"].value_counts().reset_index()
    a.columns = ["Action", "Count"]
    a["Share"] = (a["Count"] / len(df)).map(lambda x: f"{x:.1%}")
    st.dataframe(a, use_container_width=True, hide_index=True, height=300)

st.markdown("---")

# ═════════════════════════════════════════════════════════════════════════════
# Emotional state variation
# ═════════════════════════════════════════════════════════════════════════════
st.markdown(
    '<div class="sec">Emotional state variation — before vs after stimulus</div>',
    unsafe_allow_html=True,
)
st.markdown(
    '<div class="filter-note">'
    'Filters: Personality · Event — '
    'Results: mean ΔP / ΔA / ΔD, % octant shift, and P(Emotion).'
    '</div>',
    unsafe_allow_html=True,
)

c1a, c1b = st.columns(2)
with c1a:
    sel1_pers = multiselect_all("Personalities", ALL_PERS, "s1_pers")
with c1b:
    sel1_evts = multiselect_all("Events", ALL_EVTS, "s1_evt")

sub1 = df[df["Personality"].isin(sel1_pers) & df["EventID"].isin(sel1_evts)]

if sub1.empty:
    st.warning("No records match these filters.")
else:
    n1 = len(sub1)

    m1, m2, m3, m4 = st.columns(4)
    delta_card(m1, "Δ Pleasure (mean)",  sub1["DeltaP"].mean())
    delta_card(m2, "Δ Arousal (mean)",   sub1["DeltaA"].mean())
    delta_card(m3, "Δ Dominance (mean)", sub1["DeltaD"].mean())

    pct = sub1["OctantChanged"].mean()
    m4.markdown(
        f"<div style='text-align:center;padding:10px;background:#f4f8fd;"
        f"border:1px solid #d0e4f7;border-radius:8px'>"
        f"<div style='font-size:11px;color:#555;font-weight:600'>Octant shifted</div>"
        f"<div style='font-size:22px;font-weight:700;color:#2E75B6'>{pct:.1%}</div>"
        f"<div style='font-size:10px;color:#888'>of agents changed mood octant</div>"
        f"</div>",
        unsafe_allow_html=True,
    )

    st.markdown("<br>", unsafe_allow_html=True)

    t1a, t1b = st.columns(2)

    with t1a:
        st.markdown("**Mood octant — before → after**")
        oct_tbl = (
            sub1.groupby(["PrevOctant", "NewOctant"])
                .size()
                .reset_index(name="Count")
        )
        oct_tbl["Share"] = (oct_tbl["Count"] / n1).map(lambda x: f"{x:.1%}")
        st.dataframe(
            oct_tbl.sort_values("Count", ascending=False).reset_index(drop=True),
            use_container_width=True, hide_index=True, height=260,
        )

    with t1b:
        st.markdown("**P(Emotion) — probability each emotion is elicited**")
        st.plotly_chart(
            prob_bar(sub1["Emotion"], EMOTION_COLOURS, height=260),
            use_container_width=True,
        )

    st.markdown(
        f"<div class='filter-note'>Based on {n1:,} records.</div>",
        unsafe_allow_html=True,
    )

st.markdown("---")


# ═════════════════════════════════════════════════════════════════════════════
# Action probability by personality
# ═════════════════════════════════════════════════════════════════════════════
st.markdown(
    '<div class="sec">Action probability by personality</div>',
    unsafe_allow_html=True,
)
st.markdown(
    '<div class="filter-note">'
    'Required: Personality · Event — '
    'Optional: Emotion · Mood Octant (enable below). '
    'Hover over a bar to see the mean OCEAN trait values of agents who performed that action.'
    '</div>',
    unsafe_allow_html=True,
)

c2a, c2b = st.columns(2)
with c2a:
    sel2_pers = multiselect_all("Personalities", ALL_PERS, "s2_pers")
with c2b:
    sel2_evts = multiselect_all("Events", ALL_EVTS, "s2_evt")

use_emo = st.checkbox("Enable Emotion filter (optional)", value=False, key="use_emo")
use_oct = st.checkbox("Enable Mood Octant filter (optional)", value=False, key="use_oct")

sel2_emos = ALL_EMOS
sel2_oct  = ALL_OCT

if use_emo:
    sel2_emos = st.multiselect("Emotions", ALL_EMOS, default=ALL_EMOS, key="s2_emo")
    if not sel2_emos:
        sel2_emos = ALL_EMOS

if use_oct:
    sel2_oct = st.multiselect("New Mood Octant", ALL_OCT, default=ALL_OCT, key="s2_oct")
    if not sel2_oct:
        sel2_oct = ALL_OCT

sub2 = df[
    df["Personality"].isin(sel2_pers) &
    df["EventID"].isin(sel2_evts) &
    df["Emotion"].isin(sel2_emos) &
    df["NewOctant"].isin(sel2_oct)
]

if sub2.empty:
    st.warning("No records match these filters.")
else:
    n2       = len(sub2)
    counts2  = sub2["Action"].value_counts()
    probs2   = counts2 / counts2.sum()

    # Mean OCEAN per action — used both in the chart tooltip and the table below
    ocean_means2 = (
        sub2.groupby("Action")[OCEAN_TRAITS]
            .mean()
            .round(3)
            .reindex(counts2.index)
    )

    c2c, c2d = st.columns([2, 1])

    with c2c:
        st.markdown("**P(Action)** — hover for mean OCEAN values")
        # Pass the full filtered dataframe so action_prob_bar can compute means
        st.plotly_chart(
            action_prob_bar(sub2, ACTION_COLOURS,
                            height=max(220, len(counts2) * 54)),
            use_container_width=True,
        )

    with c2d:
        st.markdown("**Action breakdown + mean OCEAN**")
        act_tbl = pd.DataFrame({
            "Action":    counts2.index,
            "Count":     counts2.values,
            "P(action)": [f"{v:.1%}" for v in probs2.values],
            "O":  ocean_means2["Openness"].values,
            "C":  ocean_means2["Conscientiousness"].values,
            "E":  ocean_means2["Extraversion"].values,
            "A":  ocean_means2["Agreeableness"].values,
            "N":  ocean_means2["Neuroticism"].values,
        })
        st.dataframe(
            act_tbl.reset_index(drop=True),
            use_container_width=True,
            hide_index=True,
            height=max(220, len(counts2) * 54),
            column_config={
                "O": st.column_config.NumberColumn("O", format="%.3f"),
                "C": st.column_config.NumberColumn("C", format="%.3f"),
                "E": st.column_config.NumberColumn("E", format="%.3f"),
                "A": st.column_config.NumberColumn("A", format="%.3f"),
                "N": st.column_config.NumberColumn("N", format="%.3f"),
            },
        )

    st.markdown(
        f"<div class='filter-note'>Based on {n2:,} records.</div>",
        unsafe_allow_html=True,
    )

st.markdown("---")
