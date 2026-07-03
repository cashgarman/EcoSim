export function $(id)
{
  return document.getElementById(id);
}

export function bindEl(id, event, handler, options)
{
  const el = $(id);
  if (!el)
  {
    console.warn(`Wildlands: missing #${id}, skipping ${event} listener`);
    return null;
  }
  el.addEventListener(event, handler, options);
  return el;
}
