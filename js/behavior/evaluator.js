import { evaluateCondition } from './conditions.js';

export function evaluateTree(node, ctx)
{
  if (!node) return null;

  if (node.type === 'action')
  {
    return { nodeId: node.id, action: node.action };
  }

  if (node.type === 'condition')
  {
    const ok = evaluateCondition(node.id, node.condition, ctx);
    return ok ? { nodeId: node.id, condition: true } : null;
  }

  if (node.type === 'sequence')
  {
    let lastAction = null;
    for (const child of node.children)
    {
      const result = evaluateTree(child, ctx);
      if (!result) return null;
      if (result.action) lastAction = result;
      if (result.condition === true) continue;
    }
    return lastAction;
  }

  if (node.type === 'selector')
  {
    for (const child of node.children)
    {
      const result = evaluateTree(child, ctx);
      if (result && result.action) return result;
    }
    return null;
  }

  return null;
}
