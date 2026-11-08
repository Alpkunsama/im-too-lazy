import React, {Children, isValidElement, useEffect, useId, useLayoutEffect, useRef, useState} from 'react';
import {createPortal} from 'react-dom';
import {Check, ChevronDown, Ellipsis} from 'lucide-react';

type Option = {value: string; label: React.ReactNode; disabled: boolean};
type Props = {
  value: string; children: React.ReactNode; disabled?: boolean; id?: string;
  'aria-label'?: string; onOpen?: () => void; onClose?: () => void;
  onChange: (event: {target: {value: string}}) => void;
};

function optionText(node: React.ReactNode): string {
  if (typeof node === 'string' || typeof node === 'number') return String(node);
  if (Array.isArray(node)) return node.map(optionText).join('');
  return isValidElement<{children?: React.ReactNode}>(node) ? optionText(node.props.children) : '';
}

function useAnchoredList(open: boolean, trigger: React.RefObject<HTMLElement | null>, popup: React.RefObject<HTMLDivElement | null>, alignEnd = false) {
  useLayoutEffect(() => {
    if (!open) return;
    function position() {
      const anchor = trigger.current;
      const list = popup.current;
      if (!anchor || !list) return;
      const rect = anchor.getBoundingClientRect();
      const edge = 8, gap = 5;
      const below = window.innerHeight - rect.bottom - edge - gap;
      const above = rect.top - edge - gap;
      const flip = below < Math.min(list.scrollHeight + 10, 275) && above > below;
      const width = Math.min(window.innerWidth - edge * 2, Math.max(180, rect.width));
      list.style.width = `${width}px`;
      list.style.left = `${Math.max(edge, Math.min(alignEnd ? rect.right - width : rect.left, window.innerWidth - width - edge))}px`;
      list.style.maxHeight = `${Math.max(0, Math.min(320, flip ? above : below))}px`;
      list.style.top = flip ? 'auto' : `${rect.bottom + gap}px`;
      list.style.bottom = flip ? `${window.innerHeight - rect.top + gap}px` : 'auto';
    }
    position();
    window.addEventListener('resize', position);
    window.addEventListener('scroll', position, true);
    return () => {
      window.removeEventListener('resize', position);
      window.removeEventListener('scroll', position, true);
    };
  }, [open, trigger, popup, alignEnd]);
}

export function Select({value, children, disabled, id, 'aria-label': label, onOpen, onClose, onChange}: Props) {
  const listId = useId();
  const trigger = useRef<HTMLButtonElement>(null);
  const popup = useRef<HTMLDivElement>(null);
  const search = useRef({text: '', at: 0});
  const [snapshot, setSnapshot] = useState<Option[] | null>(null);
  const [active, setActive] = useState(-1);
  const open = snapshot !== null;
  const options = Children.toArray(children).flatMap(child => {
    if (!isValidElement<{value: string; children: React.ReactNode; disabled?: boolean}>(child) || child.type !== 'option') return [];
    return [{value: String(child.props.value), label: child.props.children, disabled: !!child.props.disabled}];
  });
  const visible = snapshot ?? options;
  const selected = visible.find(option => option.value === value) ?? visible[0];
  const locked = disabled || !options.some(option => !option.disabled);
  const closeCallback = useRef(onClose);
  closeCallback.current = onClose;

  function close() {
    setSnapshot(null);
    closeCallback.current?.();
  }

  function show(last = false) {
    if (locked) return;
    search.current = {text: '', at: 0};
    const current = options.findIndex(option => option.value === value && !option.disabled);
    const enabled = options.map((option, index) => option.disabled ? -1 : index).filter(index => index >= 0);
    setActive(current >= 0 ? current : enabled[last ? enabled.length - 1 : 0]);
    setSnapshot(options);
    onOpen?.();
  }

  function choose(option: Option) {
    if (option.disabled || disabled) return;
    close();
    if (option.value !== value) onChange({target: {value: option.value}});
  }

  useAnchoredList(open, trigger, popup);

  useEffect(() => {
    if (!open) return;
    function outside(event: PointerEvent) {
      const target = event.target as Node;
      if (!trigger.current?.contains(target) && !popup.current?.contains(target)) {
        setSnapshot(null);
        closeCallback.current?.();
      }
    }
    document.addEventListener('pointerdown', outside, true);
    return () => document.removeEventListener('pointerdown', outside, true);
  }, [open]);

  useLayoutEffect(() => {
    const list = popup.current;
    const item = list?.children[active] as HTMLElement | undefined;
    if (!open || !list || !item) return;
    if (item.offsetTop < list.scrollTop) list.scrollTop = item.offsetTop;
    else if (item.offsetTop + item.offsetHeight > list.scrollTop + list.clientHeight)
      list.scrollTop = item.offsetTop + item.offsetHeight - list.clientHeight;
  }, [open, active]);

  function keyDown(event: React.KeyboardEvent<HTMLButtonElement>) {
    const {key} = event;
    if (!open) {
      if (key === 'ArrowDown' || key === 'ArrowUp') {event.preventDefault(); show(key === 'ArrowUp');}
      return;
    }
    if (key === 'Tab') {close(); return;}
    if (key === 'Escape') {event.preventDefault(); event.stopPropagation(); close(); return;}
    if (key === 'Enter' || key === ' ') {event.preventDefault(); if (visible[active]) choose(visible[active]); return;}
    if (['ArrowUp', 'ArrowDown', 'Home', 'End'].includes(key)) {
      event.preventDefault();
      const step = key === 'ArrowUp' || key === 'End' ? -1 : 1;
      let next = key === 'Home' ? 0 : key === 'End' ? visible.length - 1 : active + step;
      while (next >= 0 && next < visible.length && visible[next].disabled) next += step;
      if (next >= 0 && next < visible.length) setActive(next);
    } else if (key.length === 1 && !event.ctrlKey && !event.altKey && !event.metaKey) {
      const now = Date.now();
      const prefix = (now - search.current.at < 600 ? search.current.text : '') + key.toLocaleLowerCase();
      search.current = {text: prefix, at: now};
      const next = visible.findIndex(option => !option.disabled && optionText(option.label).trim().toLocaleLowerCase().startsWith(prefix));
      if (next >= 0) {event.preventDefault(); setActive(next);}
    }
  }

  return <>
    <button ref={trigger} id={id} type="button" className="studio-select" role="combobox"
      disabled={locked} aria-label={label} aria-haspopup="listbox" aria-expanded={open}
      aria-controls={open ? listId : undefined} aria-activedescendant={open && active >= 0 ? `${listId}-${active}` : undefined}
      title={optionText(selected?.label)} onClick={() => open ? close() : show()} onKeyDown={keyDown} onBlur={() => {if (open) close();}}>
      <span>{selected?.label ?? '—'}</span><ChevronDown size={14} aria-hidden="true"/>
    </button>
    {open && createPortal(<div ref={popup} className="studio-option-list" id={listId} role="listbox" aria-label={label}
      >
      {visible.map((option, index) => <div key={`${option.value}:${index}`} id={`${listId}-${index}`} role="option"
        aria-selected={option.value === value} aria-disabled={option.disabled || undefined}
        className={index === active ? 'highlighted' : undefined}
        onMouseDown={event => event.preventDefault()}
        onMouseMove={() => {if (!option.disabled) setActive(index);}} onClick={() => choose(option)}>
        <span>{option.label}</span>{option.value === value && <Check size={14} aria-hidden="true"/>}
      </div>)}
    </div>, document.body)}
  </>;
}

export function ActionMenu({label, items}: {label: string; items: {id: string; label: string; action: () => void}[]}) {
  const id = useId();
  const trigger = useRef<HTMLButtonElement>(null);
  const popup = useRef<HTMLDivElement>(null);
  const [open, setOpen] = useState(false);
  useAnchoredList(open, trigger, popup, true);
  useLayoutEffect(() => {if (open) popup.current?.querySelector('button')?.focus({preventScroll:true});}, [open]);
  useEffect(() => {
    if (!open) return;
    function outside(event: PointerEvent) {
      const target = event.target as Node;
      if (!trigger.current?.contains(target) && !popup.current?.contains(target)) setOpen(false);
    }
    document.addEventListener('pointerdown', outside, true);
    return () => document.removeEventListener('pointerdown', outside, true);
  }, [open]);
  function dismiss() {setOpen(false); trigger.current?.focus({preventScroll:true});}
  return <>
    <button type="button" className="studio-action-trigger" ref={trigger} title={label} aria-label={label}
      aria-haspopup="menu" aria-controls={open ? id : undefined} aria-expanded={open}
      onClick={() => setOpen(!open)} onKeyDown={event => {if (event.key === 'ArrowDown') {event.preventDefault(); setOpen(true);}}}>
      <Ellipsis size={16} aria-hidden="true"/>
    </button>
    {open && createPortal(<div ref={popup} className="studio-option-list studio-action-menu" id={id} role="menu" aria-label={label}
      onBlur={event => {if (!popup.current?.contains(event.relatedTarget)) setOpen(false);}}
      onKeyDown={event => {
        if (event.key === 'Escape') {event.preventDefault(); event.stopPropagation(); dismiss(); return;}
        if (event.key === 'Tab') {dismiss(); return;}
        if (!['ArrowDown','ArrowUp','Home','End'].includes(event.key)) return;
        event.preventDefault();
        const buttons = Array.from(popup.current?.querySelectorAll('button') ?? []);
        const index = buttons.indexOf(document.activeElement as HTMLButtonElement);
        const next = event.key === 'Home' ? 0 : event.key === 'End' ? buttons.length - 1 : (index + (event.key === 'ArrowDown' ? 1 : -1) + buttons.length) % buttons.length;
        buttons[next]?.focus();
      }}>
      {items.map(item => <button key={item.id} type="button" role="menuitem" tabIndex={-1} onClick={() => {dismiss(); item.action();}}>{item.label}</button>)}
    </div>, document.body)}
  </>;
}
