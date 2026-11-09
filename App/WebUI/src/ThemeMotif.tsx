import {useId} from 'react';

function Contours() {
    const outlineId = useId();
    const scales = [.35, .5, .65, .8, .95, 1.1, 1.25, 1.4];

    return <>
        <defs>
            <path id={outlineId} d="M-138 8C-134-50-82-42-43-76C-10-106 18-84 50-63C78-45 126-55 145-17C169 29 109 62 68 57C23 53 12 87-32 89C-82 91-142 60-138 8Z"/>
        </defs>
        <g transform="translate(51 47) rotate(-18)">
            {scales.map(scale => <use key={scale} href={`#${outlineId}`} transform={`scale(${scale})`}/>)}
        </g>
        <g transform="translate(601 251) rotate(-27)">
            {scales.map(scale => <use key={scale} href={`#${outlineId}`} transform={`scale(${scale})`}/>)}
        </g>
    </>;
}

function Jellyfish() {
    return <>
        <path fill="currentColor" fillOpacity=".16" d="M-40 0C-39-22-23-39 0-39C23-39 39-22 40 0Q20 12 0 5Q-20 12-40 0Z"/>
        <path d="M-27 5C-19 20-40 24-30 41S-31 64-34 70M-10 7C-20 23 5 31-6 46S-17 66-10 82M10 7C2 23 26 28 15 45S9 62 16 76M27 5C19 19 42 25 31 42S29 60 35 66M-24-13Q0-28 24-13"/>
    </>;
}

function DeepSea() {
    return <>
        <g transform="translate(87 102) rotate(-16) scale(1.2)"><Jellyfish/></g>
        <g transform="translate(547 85) rotate(19) scale(.85)"><Jellyfish/></g>
        <g transform="translate(465 248) rotate(-13) scale(.56)" opacity=".64"><Jellyfish/></g>
        <g opacity=".75">
            <circle cx="39" cy="42" r="5"/><circle cx="139" cy="45" r="8"/><circle cx="168" cy="22" r="4"/>
            <circle cx="224" cy="219" r="8"/><circle cx="244" cy="187" r="4"/><circle cx="213" cy="254" r="3"/>
            <circle cx="486" cy="27" r="6"/><circle cx="591" cy="35" r="4"/><circle cx="592" cy="207" r="7"/>
            <circle cx="613" cy="179" r="4"/><circle cx="507" cy="208" r="5"/><circle cx="394" cy="277" r="4"/>
        </g>
    </>;
}

function Blueprint() {
    const gridId = useId();

    return <>
        <defs>
            <pattern id={gridId} width="24" height="24" patternUnits="userSpaceOnUse">
                <path d="M0 24V0H24" opacity=".4"/>
            </pattern>
        </defs>
        <rect width="640" height="320" stroke="none" fill={`url(#${gridId})`}/>
        <g transform="translate(104 100)">
            <rect x="-58" y="-58" width="116" height="116"/>
            <circle r="48"/><circle r="33"/><circle r="11"/>
            <path d="M-77 0H77M0-77V77" strokeDasharray="7 5"/>
            <path d="M-58 70V88M58 70V88M-58 80H58M-50 76L-58 80L-50 84M50 76L58 80L50 84"/>
        </g>
        <g transform="translate(547 209)">
            <path d="M0-70L65-36V35L0 70L-65 35V-36ZM-65-36L0 0L65-36M0 0V70"/>
            <path d="M-65 35L0 0L65 35M0 0V-70" strokeDasharray="5 5" opacity=".6"/>
            <path d="M-82-39H-72M-82 35H-72M-77-39V35M-9 84H9M0 78V90"/>
        </g>
    </>;
}

export default function ThemeMotif({theme}: {theme: string}) {
    const Motif = theme === 'topography' ? Contours
        : theme === 'deep-sea' ? DeepSea
        : theme === 'blueprint' ? Blueprint
        : null;

    if (!Motif) return null;

    return <svg className="theme-motif" viewBox="0 0 640 320" preserveAspectRatio="none"
        fill="none" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
        <Motif/>
    </svg>;
}
